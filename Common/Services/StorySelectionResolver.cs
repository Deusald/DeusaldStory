using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Resolves the variables a <see cref="StoryLogicNode.AcceptVariables">receiving</see> logic node reads from an
    /// upstream <see cref="StoryLogicExitMode.SinglePath"/> node over its VFlow output. It follows the container wire
    /// arriving at the receiver's <see cref="StoryLogicNode.VariablesIn"/> back to the upstream node's
    /// <see cref="StoryLogicNode.VFlowOut"/> — across any number of container and portal boundaries
    /// (see <see cref="ResolveVFlowSource"/>) — and exposes that node's declared variables (surfaced by the receiver's
    /// Prev Exit Variable node as constants) and the value each upstream choice pins — the Gamebook expands the
    /// receiver into one section per upstream choice.
    /// </summary>
    [PublicAPI]
    public static class StorySelectionResolver
    {
        /// <summary>How many boundaries the VFlow walk may cross before giving up (a malformed graph can't hang it).</summary>
        private const int _MAX_WALK_DEPTH = 64;

        /// <summary>The upstream SinglePath node feeding <paramref name="logic"/>'s Variables input, or null when none is wired.</summary>
        public static StoryLogicNode? SourceNode(StoryProject project, StoryLogicNode logic)
        {
            if (!logic.AcceptVariables) return null;
            if (!project.ContainerNodes.TryGetValue(logic.ParentContainer, out StoryContainerNode? parent)) return null;

            // The VFlow (variables) arrives at the node's single entry when it accepts variables.
            StoryConnection? wire = parent.Connections.Find(c => c.ToPoint == logic.EntryPoint.Id);
            if (wire is null) return null;

            return ResolveVFlowSource(project, parent, wire.FromPoint);
        }

        /// <summary>
        /// Walks <paramref name="fromPoint"/> — an output port in <paramref name="container"/> — back to the SinglePath
        /// logic node whose VFlow output ultimately feeds it, or null when nothing upstream carries variables.
        /// <para>
        /// A VFlow wire carries the variables an upstream node put on it, so how many boundaries it crosses on the way
        /// is irrelevant to what arrives: the walk follows it back through a child container's exit point (down into the
        /// child), a container's own entry point (up into the parent) and a portal's out point — as many levels deep as
        /// it takes.
        /// </para>
        /// </summary>
        public static StoryLogicNode? ResolveVFlowSource(StoryProject project, StoryContainerNode container, Guid fromPoint)
        {
            return Walk(project, container, fromPoint, new HashSet<Guid>(), 0);
        }

        private static StoryLogicNode? Walk(StoryProject project, StoryContainerNode container, Guid fromPoint,
                                            HashSet<Guid> visited, int depth)
        {
            if (fromPoint == Guid.Empty || depth > _MAX_WALK_DEPTH || !visited.Add(fromPoint)) return null;

            // A SinglePath logic node's VFlow output — the source we were walking back to.
            foreach (Guid id in container.Logic)
                if (project.LogicNodes.TryGetValue(id, out StoryLogicNode? candidate)
                 && candidate.ExitMode == StoryLogicExitMode.SinglePath
                 && candidate.VFlowOut.Id == fromPoint)
                    return candidate;

            // A child container's exit point — descend and keep walking from whatever feeds it inside the child.
            foreach (Guid id in container.Containers)
                if (project.ContainerNodes.TryGetValue(id, out StoryContainerNode? child)
                 && child.ExitPoints.Exists(p => p.Id == fromPoint))
                    return WalkInto(project, child, fromPoint, visited, depth);

            // This container's own entry point — the variables came from outside, so ascend.
            if (container.EntryPoints.Exists(p => p.Id == fromPoint))
                return WalkUp(project, container, fromPoint, visited, depth);

            // A portal's out point — flow re-emerges here from any of its ins.
            foreach (Guid id in container.Portals)
                if (project.PortalNodes.TryGetValue(id, out StoryPortalNode? portal) && portal.OutPoint.Id == fromPoint)
                    return WalkPortal(project, container, portal, visited, depth);

            return null;
        }

        /// <summary>Descends into <paramref name="child"/> and continues from whatever is wired to its <paramref name="exitPoint"/>.</summary>
        private static StoryLogicNode? WalkInto(StoryProject project, StoryContainerNode child, Guid exitPoint,
                                                HashSet<Guid> visited, int depth)
        {
            StoryConnection? wire = child.Connections.Find(c => c.ToPoint == exitPoint);
            return wire is null ? null : Walk(project, child, wire.FromPoint, visited, depth + 1);
        }

        /// <summary>Leaves <paramref name="container"/> through its <paramref name="entryPoint"/> into its parent.</summary>
        private static StoryLogicNode? WalkUp(StoryProject project, StoryContainerNode container, Guid entryPoint,
                                              HashSet<Guid> visited, int depth)
        {
            if (!project.ContainerNodes.TryGetValue(container.ParentContainer, out StoryContainerNode? parent)) return null;

            StoryConnection? wire = parent.Connections.Find(c => c.ToPoint == entryPoint);
            return wire is null ? null : Walk(project, parent, wire.FromPoint, visited, depth + 1);
        }

        /// <summary>A portal relays every in point to its single out, so its ins must agree on one source to name one.</summary>
        private static StoryLogicNode? WalkPortal(StoryProject project, StoryContainerNode container, StoryPortalNode portal,
                                                  HashSet<Guid> visited, int depth)
        {
            StoryLogicNode? agreed = null;
            foreach (StoryConnectionPoint inPoint in portal.InPoints)
            {
                StoryConnection? wire = container.Connections.Find(c => c.ToPoint == inPoint.Id);
                if (wire is null) continue;

                // Each in is its own path back, so it gets its own visited set — one branch must not block the next.
                StoryLogicNode? found = Walk(project, container, wire.FromPoint, new HashSet<Guid>(visited), depth + 1);
                if (found is null) continue;
                if (agreed is not null && agreed.Id != found.Id) return null; // ins disagree
                agreed = found;
            }
            return agreed;
        }

        /// <summary>The variables <paramref name="logic"/> receives, as its Prev Exit Variable exposes them.</summary>
        public static List<StoryDeclaredVariable> IncomingVariables(StoryProject project, StoryLogicNode logic)
        {
            if (!logic.AcceptVariables) return new List<StoryDeclaredVariable>();
            return SourceNode(project, logic)?.DeclaredVariables ?? new List<StoryDeclaredVariable>();
        }

        /// <summary>
        /// The value bound to a Prev Exit Variable output <paramref name="portId"/> from the section/preview
        /// <paramref name="values"/> (which are keyed by the upstream's declared-variable ids, so the port id is the
        /// upstream id directly).
        /// </summary>
        public static string IncomingValue(StoryProject project, StoryLogicNode logic, Guid portId, IReadOnlyDictionary<Guid, string> values)
        {
            return values.TryGetValue(portId, out string? val) ? val : "";
        }

        /// <summary>The value each of <paramref name="source"/>'s declared variables takes for one of its <paramref name="choice"/>s, keyed by declared-variable id.</summary>
        public static Dictionary<Guid, string> ValuesForChoice(StoryLogicNode source, StoryChoice choice)
        {
            Dictionary<Guid, string> map = new();
            foreach (StoryDeclaredVariable dv in source.DeclaredVariables)
                map[dv.Id] = choice.VariableValues.Find(v => v.DeclaredVarId == dv.Id)?.Value ?? "";
            return map;
        }
    }
}
