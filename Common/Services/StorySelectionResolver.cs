using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Resolves the variables a <see cref="StoryLogicNode.AcceptVariables">receiving</see> logic node reads from an
    /// upstream <see cref="StoryLogicExitMode.SinglePath"/> node over its VFlow output. It follows the container wire
    /// arriving at the receiver's <see cref="StoryLogicNode.VariablesIn"/> back to the upstream node's
    /// <see cref="StoryLogicNode.VFlowOut"/> — across any number of container, portal and blueprint-instance boundaries
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

            if (!project.ContainerNodes.TryGetValue(logic.ParentContainer, out StoryContainerNode? parent))
                // Out of tree: this node is a Logic blueprint's definition body, so the VFlow arrives from outside
                // through the instances that reference it — they must agree on one source to name one.
                return logic.ParentContainer == Guid.Empty
                    ? WalkOutOfTree(project, logic.Id, logic.EntryPoint.Id, new HashSet<Guid>(), 0)
                    : null;

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
        /// child), a container's own entry point (up into the parent, or out through the blueprint instance it was
        /// reached by), a portal's out point, and a blueprint instance's exit port — as many levels deep as it takes.
        /// </para>
        /// </summary>
        public static StoryLogicNode? ResolveVFlowSource(StoryProject project, StoryContainerNode container, Guid fromPoint)
        {
            return Walk(project, container, fromPoint, new List<StoryBlueprintInstanceNode>(), new HashSet<Guid>(), 0);
        }

        /// <param name="instances">
        /// The blueprint instances the walk descended through, innermost last. A definition's graph is shared by every
        /// instance, so when the walk leaves one through its entry point this stack says which instance to re-emerge in.
        /// </param>
        private static StoryLogicNode? Walk(StoryProject project, StoryContainerNode container, Guid fromPoint,
                                            List<StoryBlueprintInstanceNode> instances, HashSet<Guid> visited, int depth)
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
                    return WalkInto(project, child, fromPoint, instances, visited, depth);

            // This container's own entry point — the variables came from outside, so ascend.
            if (container.EntryPoints.Exists(p => p.Id == fromPoint))
                return WalkUp(project, container, fromPoint, instances, visited, depth);

            // A portal's out point — flow re-emerges here from any of its ins.
            foreach (Guid id in container.Portals)
                if (project.PortalNodes.TryGetValue(id, out StoryPortalNode? portal) && portal.OutPoint.Id == fromPoint)
                    return WalkPortal(project, container, portal, instances, visited, depth);

            // A blueprint instance's exit port — cross into the shared definition at the matching boundary point.
            foreach (Guid id in container.Instances)
                if (project.BlueprintInstances.TryGetValue(id, out StoryBlueprintInstanceNode? instance)
                 && instance.ExitPorts.Find(p => p.Id == fromPoint) is StoryBlueprintPortMap port)
                    return WalkInstance(project, instance, port, instances, visited, depth);

            return null;
        }

        /// <summary>Descends into <paramref name="child"/> and continues from whatever is wired to its <paramref name="exitPoint"/>.</summary>
        private static StoryLogicNode? WalkInto(StoryProject project, StoryContainerNode child, Guid exitPoint,
                                                List<StoryBlueprintInstanceNode> instances, HashSet<Guid> visited, int depth)
        {
            StoryConnection? wire = child.Connections.Find(c => c.ToPoint == exitPoint);
            return wire is null ? null : Walk(project, child, wire.FromPoint, instances, visited, depth + 1);
        }

        /// <summary>
        /// Leaves <paramref name="container"/> through its <paramref name="entryPoint"/>: into its parent normally, or —
        /// for a blueprint definition, which is out of tree and has no parent — out through the instance we came in by.
        /// </summary>
        private static StoryLogicNode? WalkUp(StoryProject project, StoryContainerNode container, Guid entryPoint,
                                              List<StoryBlueprintInstanceNode> instances, HashSet<Guid> visited, int depth)
        {
            if (project.ContainerNodes.TryGetValue(container.ParentContainer, out StoryContainerNode? parent))
            {
                StoryConnection? wire = parent.Connections.Find(c => c.ToPoint == entryPoint);
                return wire is null ? null : Walk(project, parent, wire.FromPoint, instances, visited, depth + 1);
            }

            // Out of tree: a blueprint definition body. When the walk descended into it, the stack names the one
            // instance to re-emerge in; when it *started* here (the definition opened on its own) nothing does, so
            // every instance is a candidate and they must agree.
            if (instances.Count == 0) return WalkOutOfTree(project, container.Id, entryPoint, visited, depth);

            StoryBlueprintInstanceNode instance = instances[^1];
            if (instance.EntryPorts.Find(p => p.DefinitionPointId == entryPoint) is not StoryBlueprintPortMap port) return null;
            if (!project.ContainerNodes.TryGetValue(instance.ParentContainer, out StoryContainerNode? outer)) return null;

            StoryConnection? outerWire = outer.Connections.Find(c => c.ToPoint == port.Id);
            if (outerWire is null) return null;

            List<StoryBlueprintInstanceNode> popped = new(instances);
            popped.RemoveAt(popped.Count - 1);
            return Walk(project, outer, outerWire.FromPoint, popped, visited, depth + 1);
        }

        /// <summary>
        /// Leaves the definition body <paramref name="definitionNodeId"/> through its <paramref name="entryPoint"/> when
        /// the walk started inside it, so no instance stack says which instance we are in. A definition's graph is shared,
        /// so the walk emerges in <b>every</b> instance that references it and they must agree on one source — the same
        /// rule a portal's ins follow. Instances that disagree mean the definition can't name one upstream: that is what
        /// <see cref="StoryLogicNode.ExpectedVariables"/> contract mode exists for.
        /// </summary>
        private static StoryLogicNode? WalkOutOfTree(StoryProject project, Guid definitionNodeId, Guid entryPoint, HashSet<Guid> visited, int depth)
        {
            if (depth > _MAX_WALK_DEPTH) return null;

            StoryBlueprint? blueprint = null;
            foreach (StoryBlueprint b in project.Blueprints.Values)
                if (b.DefinitionNodeId == definitionNodeId)
                {
                    blueprint = b;
                    break;
                }
            if (blueprint is null) return null;

            StoryLogicNode? agreed = null;
            foreach (StoryBlueprintInstanceNode instance in project.BlueprintInstances.Values)
            {
                if (instance.BlueprintId != blueprint.Id) continue;
                if (instance.EntryPorts.Find(p => p.DefinitionPointId == entryPoint) is not StoryBlueprintPortMap port) continue;
                if (!project.ContainerNodes.TryGetValue(instance.ParentContainer, out StoryContainerNode? outer)) continue;

                StoryConnection? wire = outer.Connections.Find(c => c.ToPoint == port.Id);
                if (wire is null) continue;

                // Each instance is its own path back, so it gets its own visited set — one must not block the next.
                StoryLogicNode? found = Walk(project, outer, wire.FromPoint, new List<StoryBlueprintInstanceNode>(),
                                             new HashSet<Guid>(visited), depth + 1);
                if (found is null) continue;
                if (agreed is not null && agreed.Id != found.Id) return null; // instances disagree — declare a contract instead
                agreed = found;
            }
            return agreed;
        }

        /// <summary>A portal relays every in point to its single out, so its ins must agree on one source to name one.</summary>
        private static StoryLogicNode? WalkPortal(StoryProject project, StoryContainerNode container, StoryPortalNode portal,
                                                  List<StoryBlueprintInstanceNode> instances, HashSet<Guid> visited, int depth)
        {
            StoryLogicNode? agreed = null;
            foreach (StoryConnectionPoint inPoint in portal.InPoints)
            {
                StoryConnection? wire = container.Connections.Find(c => c.ToPoint == inPoint.Id);
                if (wire is null) continue;

                // Each in is its own path back, so it gets its own visited set — one branch must not block the next.
                StoryLogicNode? found = Walk(project, container, wire.FromPoint, new List<StoryBlueprintInstanceNode>(instances),
                                             new HashSet<Guid>(visited), depth + 1);
                if (found is null) continue;
                if (agreed is not null && agreed.Id != found.Id) return null; // ins disagree — declare a contract instead
                agreed = found;
            }
            return agreed;
        }

        /// <summary>Crosses from an instance's exit <paramref name="port"/> into the blueprint definition behind it.</summary>
        private static StoryLogicNode? WalkInstance(StoryProject project, StoryBlueprintInstanceNode instance, StoryBlueprintPortMap port,
                                                    List<StoryBlueprintInstanceNode> instances, HashSet<Guid> visited, int depth)
        {
            if (!project.Blueprints.TryGetValue(instance.BlueprintId, out StoryBlueprint? blueprint)) return null;

            // A Logic blueprint's exit boundary is the definition node's own VFlow output — that node is the source.
            if (blueprint.Kind == StoryBlueprintKind.Logic)
                return project.LogicNodes.TryGetValue(blueprint.DefinitionNodeId, out StoryLogicNode? definition)
                    && definition.ExitMode == StoryLogicExitMode.SinglePath
                    && definition.VFlowOut.Id == port.DefinitionPointId
                        ? definition
                        : null;

            if (blueprint.Kind != StoryBlueprintKind.Container) return null;
            if (!project.ContainerNodes.TryGetValue(blueprint.DefinitionNodeId, out StoryContainerNode? body)) return null;

            List<StoryBlueprintInstanceNode> pushed = new(instances) { instance };
            return WalkInto(project, body, port.DefinitionPointId, pushed, visited, depth);
        }

        /// <summary>
        /// The variables <paramref name="logic"/> receives, as its Prev Exit Variable exposes them. In <b>contract</b>
        /// mode (<see cref="StoryLogicNode.ExpectedVariables"/> non-empty) these are the node's own declared expectation
        /// — stable ids that survive rewiring and blueprint reuse; otherwise the live upstream's declared variables.
        /// </summary>
        public static List<StoryDeclaredVariable> IncomingVariables(StoryProject project, StoryLogicNode logic)
        {
            if (!logic.AcceptVariables) return new List<StoryDeclaredVariable>();
            if (logic.ExpectedVariables.Count > 0) return logic.ExpectedVariables;
            return SourceNode(project, logic)?.DeclaredVariables ?? new List<StoryDeclaredVariable>();
        }

        /// <summary>
        /// The value bound to a Prev Exit Variable output <paramref name="portId"/> from the section/preview
        /// <paramref name="values"/> (which are keyed by the upstream's declared-variable ids). In contract mode the
        /// port is an <see cref="StoryLogicNode.ExpectedVariables"/> id, so it is mapped to the upstream variable of the
        /// same <b>name</b>; otherwise the port id is the upstream id directly.
        /// </summary>
        public static string IncomingValue(StoryProject project, StoryLogicNode logic, Guid portId, IReadOnlyDictionary<Guid, string> values)
        {
            if (logic.ExpectedVariables.Count > 0)
            {
                StoryDeclaredVariable? expected = logic.ExpectedVariables.Find(d => d.Id == portId);
                if (expected is null) return "";
                StoryDeclaredVariable? upstream = SourceNode(project, logic)?.DeclaredVariables
                    .Find(v => string.Equals(v.Name, expected.Name, StringComparison.Ordinal));
                return upstream is not null && values.TryGetValue(upstream.Id, out string? v) ? v : "";
            }
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
