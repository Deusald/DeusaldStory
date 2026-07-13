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

        public static List<StorageOp> StorageOps(StoryLogicNode logic)
        {
            List<StorageOp> ops     = new();
            HashSet<Guid>   visited = new();
            Guid            from    = logic.EntryPoint.Id;

            for (int guard = 0; guard < _GUARD; ++guard)
            {
                StoryConnection? conn = logic.ContentConnections.Find(c => c.FromPoint == from);
                if (conn is null) break;
                Guid to = conn.ToPoint;
                if (!visited.Add(to)) break; // inner cycle guard

                if (logic.RegisterVariableNodes.Find(n => n.FlowIn.Id == to) is StoryRegisterVariableNode reg)
                {
                    ops.Add(new StorageOp { Kind = StorageOpKind.Register, Register = reg });
                    from = reg.FlowOut.Id;
                }
                else if (logic.SetVariableNodes.Find(n => n.FlowIn.Id == to) is StorySetVariableNode set)
                {
                    ops.Add(new StorageOp { Kind = StorageOpKind.Set, Set = set });
                    from = set.FlowOut.Id;
                }
                else if (logic.UnregisterVariableNodes.Find(n => n.FlowIn.Id == to) is StoryUnregisterVariableNode unreg)
                {
                    ops.Add(new StorageOp { Kind = StorageOpKind.Unregister, Unregister = unreg });
                    from = unreg.FlowOut.Id;
                }
                else if (logic.FlowTextNodes.Find(n => n.FlowIn.Id == to) is StoryFlowTextNode ft)
                {
                    from = ft.FlowOut.Id; // text block — pass through
                }
                else
                {
                    break; // reached an Exit or a leaf input
                }
            }

            return ops;
        }
    }
}
