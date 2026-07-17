using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>Which storage operation an LFlow-chain node performs.</summary>
    public enum StorageOpKind
    {
        Register,
        Set,
        Unregister
    }

    /// <summary>
    /// One storage operation encountered while walking a logic node's inner LFlow chain, in flow order. Build one
    /// through <see cref="For(StoryRegisterVariableNode)"/> / <see cref="For(StoryProject,StoryLogicNode,StorySetVariableNode,IReadOnlyDictionary{Guid,string},StoryRenderTarget)"/>
    /// / <see cref="For(StoryUnregisterVariableNode)"/> — a Set resolves its target against the graph, so it can't be
    /// derived from the node alone.
    /// </summary>
    public sealed class StorageOp
    {
        public StorageOpKind                Kind       { get; private set; }
        public StoryRegisterVariableNode?   Register   { get; private set; }
        public StorySetVariableNode?        Set        { get; private set; }
        public StoryUnregisterVariableNode? Unregister { get; private set; }

        /// <summary>
        /// The register-node id this operation acts on (the registered variable's identity), resolved when the op was
        /// built — a Set in <see cref="StorageVariableRefMode.ByType"/> mode names its target through a wire, so it can
        /// only be resolved with the graph at hand. <see cref="Guid.Empty"/> when the target doesn't resolve.
        /// </summary>
        public Guid TargetRegisterId { get; private set; }

        /// <summary>A register operation — it acts on the variable it defines.</summary>
        public static StorageOp For(StoryRegisterVariableNode reg) =>
            new() { Kind = StorageOpKind.Register, Register = reg, TargetRegisterId = reg.Id };

        /// <summary>A set operation, resolving the variable it writes (picked by id, or named by the wire into its Name port).</summary>
        public static StorageOp For(
            StoryProject project, StoryLogicNode logic, StorySetVariableNode set,
            IReadOnlyDictionary<Guid, string>? values = null, StoryRenderTarget target = StoryRenderTarget.App) =>
            new()
            {
                Kind             = StorageOpKind.Set,
                Set              = set,
                TargetRegisterId = StoryLogicFlow.TargetOf(project, logic, set, values, target)?.Id ?? Guid.Empty
            };

        /// <summary>An unregister operation — it acts on the variable it names by id.</summary>
        public static StorageOp For(StoryUnregisterVariableNode unreg) =>
            new() { Kind = StorageOpKind.Unregister, Unregister = unreg, TargetRegisterId = unreg.RegisteredVariableId };

        /// <summary>The inner node's own id (for selecting/navigating to it).</summary>
        public Guid InnerId =>
            Kind switch
            {
                StorageOpKind.Register   => Register?.Id   ?? Guid.Empty,
                StorageOpKind.Set        => Set?.Id        ?? Guid.Empty,
                StorageOpKind.Unregister => Unregister?.Id ?? Guid.Empty,
                _                        => Guid.Empty
            };
    }

    /// <summary>
    /// Walks a logic node's inner <b>LFlow chain</b> (from its Entry, following <see cref="StoryLogicNode.ContentConnections"/>
    /// through FlowText/Register/Set/Unregister/SetExternal nodes to the Exit node) and returns the storage operations
    /// in flow order. The chain is linear (LFlow is one-in/one-out). Also resolves the value carried by a wired variable
    /// output, shared by the Exit-node auto-resolution and the previews.
    /// </summary>
    [PublicAPI]
    public static class StoryLogicFlow
    {
        private const int _GUARD = 4096;

        private static readonly Dictionary<Guid, string> _EmptyValues = new();

        /// <summary>
        /// Resolves the string value carried by the variable output wired at <paramref name="fromPoint"/> (empty when
        /// none): an External Variable's chosen variable, a Get Variable's value/slot, a Constant Variable's value, or
        /// an incoming (Prev Exit) declared variable pinned in <paramref name="values"/> by its id.
        /// </summary>
        public static string ResolveVariableValue(
            StoryProject project, StoryLogicNode logic, Guid fromPoint,
            IReadOnlyDictionary<Guid, string> values, StoryRenderTarget target)
        {
            if (fromPoint == Guid.Empty) return "";

            // A variable may be routed through a logic portal — follow it back to the real value source.
            fromPoint = logic.ResolvePortalSource(fromPoint);

            if (logic.ExternalVariableNodes.Find(n => n.OutPoint.Id == fromPoint) is StoryExternalVariableNode ev)
            {
                StoryVariable? v = StoryBuiltInVariables.Find(ev.SelectedVariableId)
                    ?? (ev.SelectedVariableId != Guid.Empty && project.Variables.TryGetValue(ev.SelectedVariableId, out StoryVariable? found) ? found : null);
                if (v is null) return "";
                if (StoryBuiltInVariables.IsBuiltIn(v.Id)) return StoryBuiltInVariables.ValueFor(v.Id, target, values);
                return values.TryGetValue(v.Id, out string? val) ? val : v.PossibleValues.FirstOrDefault() ?? "";
            }

            // A Get Variable reads a storage variable whose value is live/unknown at build time, so a condition
            // branches on a representative preview value: the node's own, or — when blank — the register's default.
            if (logic.GetVariableNodes.Find(n => n.OutPoint.Id == fromPoint) is StoryGetVariableNode gv)
                return !string.IsNullOrEmpty(gv.PreviewValue)
                    ? gv.PreviewValue
                    : TargetOf(project, logic, gv, values, target)?.PreviewValue ?? "";

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

        /// <summary>Finds a registered storage variable (a Register node) anywhere in the project by its id.</summary>
        private static StoryRegisterVariableNode? FindRegister(StoryProject project, Guid id)
        {
            if (id == Guid.Empty) return null;
            foreach (StoryLogicNode l in project.LogicNodes.Values)
                if (l.RegisterVariableNodes.Find(n => n.Id == id) is StoryRegisterVariableNode found)
                    return found;
            return null;
        }

        /// <summary>
        /// Finds a registered storage variable of <paramref name="type"/> by <paramref name="name"/> (case-insensitive)
        /// anywhere in the project — how a <see cref="StorageVariableRefMode.ByType"/> Get/Set names its target. Null
        /// when no variable of that type carries the name. When several registers share the name and type they are
        /// interchangeable for this lookup (only one can be active on any path), so the first is returned.
        /// </summary>
        public static StoryRegisterVariableNode? FindRegisterByName(StoryProject project, string name, StorageVariableType type)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            name = name.Trim();
            foreach (StoryLogicNode l in project.LogicNodes.Values)
                if (l.RegisterVariableNodes.Find(n => n.Type == type && string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase))
                    is StoryRegisterVariableNode found)
                    return found;
            return null;
        }

        /// <summary>
        /// Every value the constant output wired at <paramref name="fromPoint"/> can carry — one entry for a Constant
        /// Variable node, one per possible value for a constant External Variable. Null when the wire is missing or its
        /// source isn't constant, so the value cannot be known before play. Used to validate a
        /// <see cref="StorageVariableRefMode.ByType"/> Get/Set's wired name against <i>every</i> name it might take.
        /// </summary>
        public static List<string>? PossibleVariableValues(StoryProject project, StoryLogicNode logic, Guid fromPoint)
        {
            if (fromPoint == Guid.Empty) return null;
            fromPoint = logic.ResolvePortalSource(fromPoint);

            if (logic.ConstantVariableNodes.Find(n => n.OutPoint.Id == fromPoint) is StoryConstantVariableNode cv)
                return new List<string> { cv.Value };

            if (logic.ExternalVariableNodes.Find(n => n.OutPoint.Id == fromPoint) is StoryExternalVariableNode ev)
            {
                StoryVariable? v = StoryBuiltInVariables.Find(ev.SelectedVariableId)
                    ?? (project.Variables.TryGetValue(ev.SelectedVariableId, out StoryVariable? found) ? found : null);
                // A built-in's value is the render target, never a variable name — not a usable name source.
                if (v is null || StoryBuiltInVariables.IsBuiltIn(v.Id) || !v.IsConstant) return null;
                return new List<string>(v.PossibleValues);
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

        /// <summary>The storage variable a Get node reads — the picked register, or the one its wired name selects.</summary>
        public static StoryRegisterVariableNode? TargetOf(
            StoryProject project, StoryLogicNode logic, StoryGetVariableNode gv,
            IReadOnlyDictionary<Guid, string>? values = null, StoryRenderTarget target = StoryRenderTarget.App) =>
            TargetOf(project, logic, gv.RefMode, gv.RefType, gv.RegisteredVariableId, gv.NameIn.Id, values, target);

        /// <summary>The storage variable a Set node writes — the picked register, or the one its wired name selects.</summary>
        public static StoryRegisterVariableNode? TargetOf(
            StoryProject project, StoryLogicNode logic, StorySetVariableNode set,
            IReadOnlyDictionary<Guid, string>? values = null, StoryRenderTarget target = StoryRenderTarget.App) =>
            TargetOf(project, logic, set.RefMode, set.RefType, set.RegisteredVariableId, set.NameIn.Id, values, target);

        private static StoryRegisterVariableNode? TargetOf(
            StoryProject project, StoryLogicNode logic, StorageVariableRefMode mode, StorageVariableType type,
            Guid registeredVariableId, Guid nameIn, IReadOnlyDictionary<Guid, string>? values, StoryRenderTarget target)
        {
            if (mode == StorageVariableRefMode.Specific) return FindRegister(project, registeredVariableId);
            string name = ResolveVariableValue(project, logic, FromInto(logic, nameIn), values ?? _EmptyValues, target);
            return FindRegisterByName(project, name, type);
        }

        public static List<StorageOp> StorageOps(
            StoryProject project, StoryLogicNode logic, StoryRenderTarget target = StoryRenderTarget.App,
            IReadOnlyDictionary<Guid, string>? values = null)
        {
            values ??= _EmptyValues;
            List<StorageOp> ops     = new();
            HashSet<Guid>   visited = new(); // node ids already emitted (so we don't duplicate them below)
            HashSet<Guid>   hops    = new(); // to-points already followed (inner cycle guard; also bounds condition recursion)

            // Walk the linear LFlow chain so operations placed on it keep their reading order. A Condition node detours
            // into its "condition true" block (up to the paired End node) when the condition holds, then continues.
            void Walk(Guid from)
            {
                for (int guard = 0; guard < _GUARD; ++guard)
                {
                    StoryConnection? conn = logic.ContentConnections.Find(c => c.FromPoint == from);
                    if (conn is null) break;
                    Guid to = conn.ToPoint;
                    if (!hops.Add(to)) break; // inner cycle guard

                    if (logic.RegisterVariableNodes.Find(n => n.FlowIn.Id == to) is StoryRegisterVariableNode reg)
                    {
                        ops.Add(StorageOp.For(reg));
                        visited.Add(reg.Id);
                        from = reg.FlowOut.Id;
                    }
                    else if (logic.SetVariableNodes.Find(n => n.FlowIn.Id == to) is StorySetVariableNode set)
                    {
                        ops.Add(StorageOp.For(project, logic, set, values, target));
                        visited.Add(set.Id);
                        from = set.FlowOut.Id;
                    }
                    else if (logic.UnregisterVariableNodes.Find(n => n.FlowIn.Id == to) is StoryUnregisterVariableNode unreg)
                    {
                        ops.Add(StorageOp.For(unreg));
                        visited.Add(unreg.Id);
                        from = unreg.FlowOut.Id;
                    }
                    else if (logic.FlowTextNodes.Find(n => n.FlowIn.Id == to) is StoryFlowTextNode ft)
                    {
                        from = ft.FlowOut.Id; // text block — pass through
                    }
                    else if (logic.SplitForAppNodes.Find(n => n.FlowIn.Id == to) is StorySplitForAppNode split)
                    {
                        from = split.FlowOut.Id; // App page break — not a storage op, pass through
                    }
                    else if (logic.SetExternalVariableNodes.Find(n => n.FlowIn.Id == to) is StorySetExternalVariableNode se)
                    {
                        from = se.FlowOut.Id; // external-variable set — not a storage op, pass through
                    }
                    else if (logic.ConditionFlowNodes.Find(n => n.FlowIn.Id == to) is StoryConditionFlowNode cf)
                    {
                        if (EvaluateConditionFlow(project, logic, cf, values, target))
                            Walk(cf.ConditionTrueOut.Id); // inject the block; it ends at the paired End node
                        from = cf.ContinueOut.Id;         // then always continue
                    }
                    else
                    {
                        break; // reached the Exit node, an End-condition node, or a leaf input
                    }
                }
            }

            Walk(logic.EntryPoint.Id);

            // Then append storage nodes that aren't wired onto the chain at all — a node dropped inside a logic node
            // but left unwired still performs its operation when the story visits the node.
            // Register-before-Set-before-Unregister so an unwired register/unregister pair still balances.
            bool Wired(StoryConnectionPoint flowIn, StoryConnectionPoint flowOut) =>
                logic.ContentConnections.Exists(c => c.ToPoint == flowIn.Id || c.FromPoint == flowOut.Id);

            foreach (StoryRegisterVariableNode reg in logic.RegisterVariableNodes)
                if (!visited.Contains(reg.Id) && !Wired(reg.FlowIn, reg.FlowOut))
                    ops.Add(StorageOp.For(reg));
            foreach (StorySetVariableNode set in logic.SetVariableNodes)
                if (!visited.Contains(set.Id) && !Wired(set.FlowIn, set.FlowOut))
                    ops.Add(StorageOp.For(project, logic, set, values, target));
            foreach (StoryUnregisterVariableNode unreg in logic.UnregisterVariableNodes)
                if (!visited.Contains(unreg.Id) && !Wired(unreg.FlowIn, unreg.FlowOut))
                    ops.Add(StorageOp.For(unreg));

            return ops;
        }
    }
}
