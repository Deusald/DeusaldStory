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

    /// <summary>One storage operation encountered while walking a logic node's inner LFlow chain, in flow order.</summary>
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
                StorageOpKind.Register   => Register?.Id                     ?? Guid.Empty,
                StorageOpKind.Set        => Set?.RegisteredVariableId        ?? Guid.Empty,
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

            if (logic.ExternalVariableNodes.Find(n => n.OutPoint.Id == fromPoint) is StoryExternalVariableNode ev)
            {
                StoryVariable? v = StoryBuiltInVariables.Find(ev.SelectedVariableId)
                    ?? (ev.SelectedVariableId != Guid.Empty && project.Variables.TryGetValue(ev.SelectedVariableId, out StoryVariable? found) ? found : null);
                if (v is null) return "";
                if (StoryBuiltInVariables.IsBuiltIn(v.Id)) return StoryBuiltInVariables.ValueFor(target);
                return values.TryGetValue(v.Id, out string? val) ? val : v.PossibleValues.FirstOrDefault() ?? "";
            }

            // A Get Variable reads a storage variable whose value is live/unknown at build time, so a condition
            // branches on a representative preview value: the node's own, or — when blank — the register's default.
            if (logic.GetVariableNodes.Find(n => n.OutPoint.Id == fromPoint) is StoryGetVariableNode gv)
                return !string.IsNullOrEmpty(gv.PreviewValue)
                    ? gv.PreviewValue
                    : FindRegister(project, gv.RegisteredVariableId)?.PreviewValue ?? "";

            if (logic.ConstantVariableNodes.Find(n => n.OutPoint.Id == fromPoint) is StoryConstantVariableNode cv)
                return cv.Value;

            // A Prev Exit Variable output is keyed by the upstream declared-variable id; its value is pinned per section.
            return values.TryGetValue(fromPoint, out string? sel) ? sel : "";
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

            // Walk the linear LFlow chain so operations placed on it keep their reading order.
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
                else if (logic.SplitForAppNodes.Find(n => n.FlowIn.Id == to) is StorySplitForAppNode split)
                {
                    from = split.FlowOut.Id; // App page break — not a storage op, pass through
                }
                else if (logic.SetExternalVariableNodes.Find(n => n.FlowIn.Id == to) is StorySetExternalVariableNode se)
                {
                    from = se.FlowOut.Id; // external-variable set — not a storage op, pass through
                }
                else
                {
                    break; // reached the Exit node or a leaf input
                }
            }

            // Then append storage nodes that aren't wired onto the chain at all — a node dropped inside a logic node
            // but left unwired still performs its operation when the story visits the node.
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
