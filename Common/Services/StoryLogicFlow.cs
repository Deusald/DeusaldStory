using System;
using System.Collections.Generic;
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

        public static List<StorageOp> StorageOps(StoryLogicNode logic, StoryRenderTarget target = StoryRenderTarget.App)
        {
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
                else
                {
                    break; // reached an Exit, a Choice, or a leaf input
                }
            }

            // Then append any storage nodes that aren't wired onto the spine — a node placed inside a logic node
            // still performs its operation when the story visits the node, whether or not the author wired its flow
            // ports. Register-before-Set-before-Unregister so an unwired register/unregister pair still balances.
            foreach (StoryRegisterVariableNode reg in logic.RegisterVariableNodes)
                if (!visited.Contains(reg.Id))
                    ops.Add(new StorageOp { Kind = StorageOpKind.Register, Register = reg });
            foreach (StorySetVariableNode set in logic.SetVariableNodes)
                if (!visited.Contains(set.Id))
                    ops.Add(new StorageOp { Kind = StorageOpKind.Set, Set = set });
            foreach (StoryUnregisterVariableNode unreg in logic.UnregisterVariableNodes)
                if (!visited.Contains(unreg.Id))
                    ops.Add(new StorageOp { Kind = StorageOpKind.Unregister, Unregister = unreg });

            return ops;
        }
    }
}
