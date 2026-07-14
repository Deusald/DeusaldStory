using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>Which storage operation a flow-spine node performs.</summary>
    public enum StorageOpKind
    {
        Register,
        Set,
        Unregister
    }

    /// <summary>One storage operation encountered while walking a logic node's inner flow spine, in flow order.</summary>
    public sealed class StorageOp
    {
        public StorageOpKind                Kind       { get; set; }
        public StoryRegisterVariableNode?   Register   { get; set; }
        public StorySetVariableNode?        Set        { get; set; }
        public StoryUnregisterVariableNode? Unregister { get; set; }

        /// <summary>The register-node id this operation acts on (the registered variable's identity).</summary>
        public Guid TargetRegisterId =>
            Kind switch
            {
                StorageOpKind.Register   => Register?.Id                   ?? Guid.Empty,
                StorageOpKind.Set        => Set?.RegisteredVariableId      ?? Guid.Empty,
                StorageOpKind.Unregister => Unregister?.RegisteredVariableId ?? Guid.Empty,
                _                        => Guid.Empty
            };

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
    /// Walks a logic node's inner <b>flow spine</b> (from its Entry, following <see cref="StoryLogicNode.ContentConnections"/>
    /// through FlowText/Register/Set/Unregister nodes) and returns the storage operations in the order flow reaches
    /// them. Shared by the graph validator and the Gamebook instruction generator so both agree on ordering.
    /// </summary>
    [PublicAPI]
    public static class StoryLogicFlow
    {
        private const int _GUARD = 4096;

        private static readonly Dictionary<Guid, string> _EmptyValues = new();

        /// <summary>
        /// Evaluates <paramref name="condition"/> against the value wired into its variable input: resolves that value
        /// from <paramref name="values"/> (an External Variable node's chosen variable, or the Prev-Exit Selection),
        /// then applies the operator. Ordering operators parse both sides to integers and fail when either isn't a
        /// number; Equal / NotEqual compare case-insensitive strings. Missing/unconnected inputs resolve to empty.
        /// </summary>
        public static bool EvaluateCondition(
            StoryProject project, StoryLogicNode logic, StoryConditionNode condition,
            IReadOnlyDictionary<Guid, string> values, StoryRenderTarget target)
        {
            string value = ResolveVariableValue(project, logic, FromInto(logic, condition.VariableIn.Id), values, target);
            string other = condition.CompareValue;

            switch (condition.Operator)
            {
                case StoryConditionOperator.Equal:    return string.Equals(value, other, StringComparison.OrdinalIgnoreCase);
                case StoryConditionOperator.NotEqual: return !string.Equals(value, other, StringComparison.OrdinalIgnoreCase);
                default:
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int a) ||
                        !int.TryParse(other, NumberStyles.Integer, CultureInfo.InvariantCulture, out int b))
                        return false;
                    return condition.Operator switch
                    {
                        StoryConditionOperator.LessThan       => a < b,
                        StoryConditionOperator.GreaterThan    => a > b,
                        StoryConditionOperator.LessOrEqual    => a <= b,
                        StoryConditionOperator.GreaterOrEqual => a >= b,
                        _                                     => false
                    };
            }
        }

        /// <summary>The flow-output id a Condition node's flow leaves through for the given value set (its True or False port).</summary>
        public static Guid ConditionBranch(
            StoryProject project, StoryLogicNode logic, StoryConditionNode condition,
            IReadOnlyDictionary<Guid, string> values, StoryRenderTarget target) =>
            EvaluateCondition(project, logic, condition, values, target) ? condition.FlowTrue.Id : condition.FlowFalse.Id;

        /// <summary>Resolves the string value carried by the variable output wired at <paramref name="fromPoint"/> (empty when none).</summary>
        private static string ResolveVariableValue(
            StoryProject project, StoryLogicNode logic, Guid fromPoint,
            IReadOnlyDictionary<Guid, string> values, StoryRenderTarget target)
        {
            if (fromPoint == Guid.Empty) return "";

            if (logic.ExternalVariableNodes.Find(n => n.OutPoint.Id == fromPoint) is StoryExternalVariableNode ev)
            {
                StoryVariable? v = StoryBuiltInVariables.Find(ev.SelectedVariableId)
                    ?? (ev.SelectedVariableId != Guid.Empty && project.Variables.TryGetValue(ev.SelectedVariableId, out StoryVariable? found) ? found : null);
                if (v is null) return "";
                if (StoryBuiltInVariables.IsBuiltIn(v.Id)) return StoryBuiltInVariables.ValueFor(target);
                return values.TryGetValue(v.Id, out string? val) ? val : v.PossibleValues.FirstOrDefault() ?? "";
            }

            // A Get Variable reads a storage variable whose value is physical/live and unknown at build time, so the
            // condition branches on a representative preview value in both mediums: the node's own, or — when blank —
            // the register's default preview value.
            if (logic.GetVariableNodes.Find(n => n.OutPoint.Id == fromPoint) is StoryGetVariableNode gv)
                return !string.IsNullOrEmpty(gv.PreviewValue)
                    ? gv.PreviewValue
                    : FindRegister(project, gv.RegisteredVariableId)?.PreviewValue ?? "";

            if (logic.LocalVariableNodes.Find(n => n.OutPoint.Id == fromPoint) is StoryLocalVariableNode lv)
                return lv.Value;

            if (fromPoint == logic.PrevExitVariable.OutPoint.Id && logic.AcceptExitVariable)
                return values.TryGetValue(logic.PrevExitVariable.Id, out string? sel) ? sel : "";

            return "";
        }

        private static Guid FromInto(StoryLogicNode logic, Guid toPoint) =>
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

        public static List<StorageOp> StorageOps(
            StoryProject project, StoryLogicNode logic, StoryRenderTarget target = StoryRenderTarget.App,
            IReadOnlyDictionary<Guid, string>? values = null)
        {
            values ??= _EmptyValues;
            List<StorageOp> ops     = new();
            HashSet<Guid>   visited = new(); // node ids already emitted (so we don't duplicate them below)
            HashSet<Guid>   hops    = new(); // to-points already followed (inner cycle guard)
            Guid            from    = logic.EntryPoint.Id;

            // First, walk the wired flow spine so operations placed on it keep their reading order.
            for (int guard = 0; guard < _GUARD; ++guard)
            {
                StoryConnection? conn = logic.ContentConnections.Find(c => c.FromPoint == from);
                if (conn is null) break;
                Guid to = conn.ToPoint;
                if (!hops.Add(to)) break; // inner cycle guard

                if (logic.RegisterVariableNodes.Find(n => n.FlowIn.Id == to) is StoryRegisterVariableNode reg)
                {
                    ops.Add(new StorageOp { Kind = StorageOpKind.Register, Register = reg });
                    visited.Add(reg.Id);
                    from = reg.FlowOut.Id;
                }
                else if (logic.SetVariableNodes.Find(n => n.FlowIn.Id == to) is StorySetVariableNode set)
                {
                    ops.Add(new StorageOp { Kind = StorageOpKind.Set, Set = set });
                    visited.Add(set.Id);
                    from = set.FlowOut.Id;
                }
                else if (logic.UnregisterVariableNodes.Find(n => n.FlowIn.Id == to) is StoryUnregisterVariableNode unreg)
                {
                    ops.Add(new StorageOp { Kind = StorageOpKind.Unregister, Unregister = unreg });
                    visited.Add(unreg.Id);
                    from = unreg.FlowOut.Id;
                }
                else if (logic.FlowTextNodes.Find(n => n.FlowIn.Id == to) is StoryFlowTextNode ft)
                {
                    from = ft.FlowOut.Id; // text block — pass through
                }
                else if (logic.SetExternalVariableNodes.Find(n => n.FlowIn.Id == to) is StorySetExternalVariableNode se)
                {
                    from = se.FlowOut.Id; // external-variable set — not a storage op, pass through
                }
                else if (logic.AppGamebookFlowSplitterNodes.Find(n => n.FlowIn.Id == to) is StoryAppGamebookFlowSplitterNode fs)
                {
                    from = target == StoryRenderTarget.App ? fs.AppFlowOut.Id : fs.GamebookFlowOut.Id; // follow the rendered medium's branch
                }
                else if (logic.ConditionNodes.Find(n => n.FlowIn.Id == to) is StoryConditionNode cond)
                {
                    from = ConditionBranch(project, logic, cond, values, target); // follow the True/False branch for these values
                }
                else
                {
                    break; // reached an Exit, a Choice, or a leaf input
                }
            }

            // Then append storage nodes that aren't wired onto the spine at all — a node dropped inside a logic node
            // but left unwired still performs its operation when the story visits the node. A node that *is* wired but
            // sits on a branch this walk didn't take (e.g. the Gamebook branch of a flow splitter while rendering the
            // App) is intentionally skipped — it belongs to the other medium's path, not this one.
            // Register-before-Set-before-Unregister so an unwired register/unregister pair still balances.
            bool Wired(StoryConnectionPoint flowIn, StoryConnectionPoint flowOut) =>
                logic.ContentConnections.Exists(c => c.ToPoint == flowIn.Id || c.FromPoint == flowOut.Id);

            foreach (StoryRegisterVariableNode reg in logic.RegisterVariableNodes)
                if (!visited.Contains(reg.Id) && !Wired(reg.FlowIn, reg.FlowOut))
                    ops.Add(new StorageOp { Kind = StorageOpKind.Register, Register = reg });
            foreach (StorySetVariableNode set in logic.SetVariableNodes)
                if (!visited.Contains(set.Id) && !Wired(set.FlowIn, set.FlowOut))
                    ops.Add(new StorageOp { Kind = StorageOpKind.Set, Set = set });
            foreach (StoryUnregisterVariableNode unreg in logic.UnregisterVariableNodes)
                if (!visited.Contains(unreg.Id) && !Wired(unreg.FlowIn, unreg.FlowOut))
                    ops.Add(new StorageOp { Kind = StorageOpKind.Unregister, Unregister = unreg });

            return ops;
        }
    }
}
