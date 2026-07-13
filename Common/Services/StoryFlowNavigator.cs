using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Resolves where the story's <b>flow</b> goes when it leaves a logic node's exit port — walking wires across
    /// container boundaries and through portals until it reaches the next logic node, the story's End, or a dead
    /// end. A container's entry/exit point ids are shared between the parent's container-node ports and the inner
    /// Entry/Exit nodes, so no id remapping is needed as the walk crosses a boundary.
    /// </summary>
    [PublicAPI]
    public static class StoryFlowNavigator
    {
        public enum NextKind
        {
            /// <summary>Flow reaches another logic node — <see cref="NextLogicResult.Logic"/> is set.</summary>
            Logic,

            /// <summary>Flow reaches the root container's End exit — the story ends here.</summary>
            End,

            /// <summary>An exit is not wired to anything (a validation error to surface in the preview).</summary>
            Dangling
        }

        public readonly struct NextLogicResult
        {
            public NextLogicResult(NextKind kind, StoryLogicNode? logic)
            {
                Kind  = kind;
                Logic = logic;
            }

            public NextKind        Kind  { get; }
            public StoryLogicNode? Logic { get; }
        }

        private const int _GUARD = 512;

        /// <summary>
        /// Follows the flow leaving the exit point <paramref name="fromExitPointId"/> (a
        /// <see cref="StoryLogicNode.ExitPoints"/> id) to the next logic node, the story End, or a dead end.
        /// </summary>
        public static NextLogicResult ResolveNextLogic(StoryProject project, Guid fromExitPointId)
        {
            Lookups lk = Lookups.Build(project);

            // The exit belongs to a logic node; its wires live in that node's parent container.
            if (!lk.LogicByExit.TryGetValue(fromExitPointId, out StoryLogicNode? owner)
                || !project.ContainerNodes.TryGetValue(owner.ParentContainer, out StoryContainerNode? container))
                return new NextLogicResult(NextKind.Dangling, null);

            return FollowFromOutput(project, lk, container, fromExitPointId, 0);
        }

        /// <summary>Follows the single wire leaving <paramref name="outputPointId"/> within <paramref name="container"/>.</summary>
        private static NextLogicResult FollowFromOutput(
            StoryProject project, Lookups lk, StoryContainerNode container, Guid outputPointId, int guard)
        {
            if (guard > _GUARD) return new NextLogicResult(NextKind.Dangling, null);

            StoryConnection? conn = container.Connections.Find(c => c.FromPoint == outputPointId);
            if (conn is null) return new NextLogicResult(NextKind.Dangling, null); // unwired exit/output

            return ArriveAt(project, lk, conn.ToPoint, guard + 1);
        }

        /// <summary>Classifies the point flow arrived at and either stops (logic/End) or hops onward.</summary>
        private static NextLogicResult ArriveAt(StoryProject project, Lookups lk, Guid pointId, int guard)
        {
            if (guard > _GUARD) return new NextLogicResult(NextKind.Dangling, null);

            // A logic node's entry — the next stop point.
            if (lk.LogicByEntry.TryGetValue(pointId, out StoryLogicNode? logic))
                return new NextLogicResult(NextKind.Logic, logic);

            // A child container's entry — descend; the entry acts as an output inside that container.
            if (lk.ContainerByEntry.TryGetValue(pointId, out StoryContainerNode? child))
                return FollowFromOutput(project, lk, child, pointId, guard + 1);

            // A container's exit — flow leaves it. The root's End ends the story; otherwise ascend to the parent,
            // where the exit acts as an output port on the container node.
            if (lk.ContainerByExit.TryGetValue(pointId, out StoryContainerNode? owner))
            {
                if (owner.Id == project.Metadata.RootStoryContainerNodeId)
                    return new NextLogicResult(NextKind.End, null);
                if (!project.ContainerNodes.TryGetValue(owner.ParentContainer, out StoryContainerNode? parent))
                    return new NextLogicResult(NextKind.Dangling, null);
                return FollowFromOutput(project, lk, parent, pointId, guard + 1);
            }

            // A portal in — teleport to the pair's single out point and continue from there.
            if (lk.PortalByIn.TryGetValue(pointId, out StoryPortalNode? portal)
                && project.ContainerNodes.TryGetValue(portal.ParentContainer, out StoryContainerNode? portalContainer))
                return FollowFromOutput(project, lk, portalContainer, portal.OutPoint.Id, guard + 1);

            return new NextLogicResult(NextKind.Dangling, null);
        }

        /// <summary>Point-id → owner lookups built once per resolution so classification is O(1).</summary>
        private sealed class Lookups
        {
            public Dictionary<Guid, StoryLogicNode>     LogicByEntry     { get; } = new();
            public Dictionary<Guid, StoryLogicNode>     LogicByExit      { get; } = new();
            public Dictionary<Guid, StoryContainerNode> ContainerByEntry { get; } = new();
            public Dictionary<Guid, StoryContainerNode> ContainerByExit  { get; } = new();
            public Dictionary<Guid, StoryPortalNode>    PortalByIn       { get; } = new();

            public static Lookups Build(StoryProject project)
            {
                Lookups lk = new();

                foreach (StoryLogicNode logic in project.LogicNodes.Values)
                {
                    lk.LogicByEntry[logic.EntryPoint.Id] = logic;
                    foreach (StoryConnectionPoint exit in logic.ExitPoints)
                        lk.LogicByExit[exit.Id] = logic;
                    // A single-selection node leaves through its one Selection flow-out, not the per-exit points.
                    lk.LogicByExit[logic.SelectionFlowOut.Id] = logic;
                }

                foreach (StoryContainerNode container in project.ContainerNodes.Values)
                {
                    foreach (StoryConnectionPoint entry in container.EntryPoints)
                        lk.ContainerByEntry[entry.Id] = container;
                    foreach (StoryConnectionPoint exit in container.ExitPoints)
                        lk.ContainerByExit[exit.Id] = container;
                }

                foreach (StoryPortalNode portal in project.PortalNodes.Values)
                    foreach (StoryConnectionPoint inPoint in portal.InPoints)
                        lk.PortalByIn[inPoint.Id] = portal;

                return lk;
            }
        }
    }
}
