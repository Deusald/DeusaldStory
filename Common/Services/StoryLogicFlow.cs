using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Walks a logic node's inner <b>LFlow chain</b> (from its Entry, following <see cref="StoryLogicNode.ContentConnections"/>
    /// through FlowText / Set / Split / Condition nodes to the Exit node) and resolves the value carried by a wired
    /// variable output. Get/Set nodes now reference a global <see cref="StoryVariable"/> directly by id (there is no
    /// register/unregister lifecycle any more), so resolution is a straight lookup in the project catalog / built-ins.
    /// </summary>
    [PublicAPI]
    public static class StoryLogicFlow
    {
        private const int _GUARD = 4096;

        private static readonly Dictionary<Guid, string> _EmptyValues = new();

        /// <summary>The global variable with <paramref name="id"/> — a built-in or a catalogued one, or null.</summary>
        public static StoryVariable? Variable(StoryProject project, Guid id)
        {
            if (id == Guid.Empty) return null;
            return StoryBuiltInVariables.Find(id)
                ?? (project.Variables.TryGetValue(id, out StoryVariable? v) ? v : null);
        }

        /// <summary>The variable a Get node reads (null when nothing picked or it no longer exists).</summary>
        public static StoryVariable? GetTarget(StoryProject project, StoryGetVariableNode gv) => Variable(project, gv.SelectedVariableId);

        /// <summary>The variable a Set node writes (null when nothing picked or it no longer exists).</summary>
        public static StoryVariable? SetTarget(StoryProject project, StorySetVariableNode set) => Variable(project, set.SelectedVariableId);

        /// <summary>
        /// Resolves the string value carried by the variable output wired at <paramref name="fromPoint"/> (empty when
        /// none): a Get Variable's value (a built-in's medium/theme, an Initial/Constant external's fixed value, else a
        /// preview/live value), a Constant Variable's value, or an incoming (Prev Exit) declared variable pinned in
        /// <paramref name="values"/> by its id.
        /// </summary>
        public static string ResolveVariableValue(
            StoryProject project, StoryLogicNode logic, Guid fromPoint,
            IReadOnlyDictionary<Guid, string> values, StoryRenderTarget target)
        {
            if (fromPoint == Guid.Empty) return "";

            // A variable may be routed through a logic portal — follow it back to the real value source.
            fromPoint = logic.ResolvePortalSource(fromPoint);

            if (logic.GetVariableNodes.Find(n => n.OutPoint.Id == fromPoint) is StoryGetVariableNode gv)
            {
                StoryVariable? v = GetTarget(project, gv);
                if (v is null) return "";
                if (StoryBuiltInVariables.IsBuiltIn(v.Id)) return StoryBuiltInVariables.ValueFor(v.Id, target, values);
                if (StoryVariableValues.IsConstant(v)) return v.FixedValue;
                // Runtime / internal value is live/unknown at build time — a representative preview value is used.
                if (!string.IsNullOrEmpty(gv.PreviewValue)) return gv.PreviewValue;
                return values.TryGetValue(v.Id, out string? val) ? val : StoryVariableValues.PossibleValues(v).FirstOrDefault() ?? "";
            }

            if (logic.ConstantVariableNodes.Find(n => n.OutPoint.Id == fromPoint) is StoryConstantVariableNode cv)
                return cv.Value;

            // A Prev Exit Variable output is keyed by the upstream declared-variable id; its value is pinned per section.
            return values.TryGetValue(fromPoint, out string? sel) ? sel : "";
        }

        /// <summary>
        /// Evaluates a condition-flow node's <see cref="StoryConditionFlowNode.Condition"/> against the (constant)
        /// variables wired into its Variables input, applying its <see cref="StoryConditionFlowNode.Negate"/> flag.
        /// The flow walkers call this to decide whether to enter the injected "condition true" block. Because only
        /// constants feed the condition, the result is the same for the App and the Gamebook.
        /// </summary>
        public static bool EvaluateConditionFlow(
            StoryProject project, StoryLogicNode logic, StoryConditionFlowNode cf,
            IReadOnlyDictionary<Guid, string> values, StoryRenderTarget target)
        {
            string Resolve(Guid varOut) => ResolveVariableValue(project, logic, varOut, values, target);
            bool   result = cf.Condition.Evaluate(Resolve);
            return cf.Negate ? !result : result;
        }

        /// <summary>The output wired into <paramref name="toPoint"/>, or <see cref="Guid.Empty"/> when the input is unconnected.</summary>
        public static Guid FromInto(StoryLogicNode logic, Guid toPoint) =>
            logic.ContentConnections.Find(c => c.ToPoint == toPoint)?.FromPoint ?? Guid.Empty;

        /// <summary>
        /// Every value the constant output wired at <paramref name="fromPoint"/> can carry — one entry for a Constant
        /// Variable node, and the fixed/known values of a Get node reading a Constant/Initial External variable. Null
        /// when the wire is missing or its source isn't constant (so the value cannot be known before play).
        /// </summary>
        public static List<string>? PossibleVariableValues(StoryProject project, StoryLogicNode logic, Guid fromPoint)
        {
            if (fromPoint == Guid.Empty) return null;
            fromPoint = logic.ResolvePortalSource(fromPoint);

            if (logic.ConstantVariableNodes.Find(n => n.OutPoint.Id == fromPoint) is StoryConstantVariableNode cv)
                return new List<string> { cv.Value };

            if (logic.GetVariableNodes.Find(n => n.OutPoint.Id == fromPoint) is StoryGetVariableNode gv)
            {
                StoryVariable? v = GetTarget(project, gv);
                // A built-in's value is the render target, and a Runtime/internal value isn't knowable up front.
                if (v is null || StoryBuiltInVariables.IsBuiltIn(v.Id) || !StoryVariableValues.IsConstant(v)) return null;
                return v.ExternalForm == StoryExternalForm.Constant
                    ? new List<string> { v.FixedValue }
                    : StoryVariableValues.PossibleValues(v);
            }

            return null;
        }

        /// <summary>
        /// Every value the branch source wired at <paramref name="fromPoint"/> can carry, for a Randomized Instruction
        /// node's per-value ranges. Extends <see cref="PossibleVariableValues"/> to also cover an upstream
        /// <b>Prev Exit</b> declared variable — the one branch source the Gamebook actually fans a section out per value
        /// for. Null when the wire is missing or the source cannot be enumerated before play.
        /// </summary>
        public static List<string>? BranchValues(StoryProject project, StoryLogicNode logic, Guid fromPoint)
        {
            if (PossibleVariableValues(project, logic, fromPoint) is List<string> constant) return constant;
            if (fromPoint == Guid.Empty) return null;

            Guid src = logic.ResolvePortalSource(fromPoint);
            StoryDeclaredVariable? incoming = StorySelectionResolver.IncomingVariables(project, logic).Find(d => d.Id == src);
            return incoming is not null ? new List<string>(incoming.PossibleValues) : null;
        }

        /// <summary>
        /// The Set nodes on <paramref name="logic"/>'s LFlow chain, in flow order (a Condition node detours into its
        /// "condition true" block when its condition holds), then any Set nodes dropped in the graph but left unwired.
        /// Both External and Internal sets are returned; the caller decides which surface a player instruction.
        /// </summary>
        public static List<StorySetVariableNode> SetNodesInOrder(
            StoryProject project, StoryLogicNode logic,
            StoryRenderTarget target = StoryRenderTarget.App, IReadOnlyDictionary<Guid, string>? values = null)
        {
            values ??= _EmptyValues;
            List<StorySetVariableNode> result  = new();
            HashSet<Guid>              visited = new();
            HashSet<Guid>              hops    = new();

            void Walk(Guid from)
            {
                for (int guard = 0; guard < _GUARD; ++guard)
                {
                    StoryConnection? conn = logic.ContentConnections.Find(c => c.FromPoint == from);
                    if (conn is null) break;
                    Guid to = conn.ToPoint;
                    if (!hops.Add(to)) break; // inner cycle guard

                    if (logic.SetVariableNodes.Find(n => n.FlowIn.Id == to) is StorySetVariableNode set)
                    {
                        result.Add(set);
                        visited.Add(set.Id);
                        from = set.FlowOut.Id;
                    }
                    else if (logic.FlowTextNodes.Find(n => n.FlowIn.Id == to) is StoryFlowTextNode ft)
                    {
                        from = ft.FlowOut.Id;
                    }
                    else if (logic.SplitForAppNodes.Find(n => n.FlowIn.Id == to) is StorySplitForAppNode split)
                    {
                        from = split.FlowOut.Id;
                    }
                    else if (logic.ConditionFlowNodes.Find(n => n.FlowIn.Id == to) is StoryConditionFlowNode cf)
                    {
                        if (EvaluateConditionFlow(project, logic, cf, values, target))
                            Walk(cf.ConditionTrueOut.Id);
                        from = cf.ContinueOut.Id;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            Walk(logic.EntryPoint.Id);

            bool Wired(StoryConnectionPoint flowIn, StoryConnectionPoint flowOut) =>
                logic.ContentConnections.Exists(c => c.ToPoint == flowIn.Id || c.FromPoint == flowOut.Id);

            foreach (StorySetVariableNode set in logic.SetVariableNodes)
                if (!visited.Contains(set.Id) && !Wired(set.FlowIn, set.FlowOut))
                    result.Add(set);

            return result;
        }
    }
}
