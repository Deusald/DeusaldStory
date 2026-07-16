using DeusaldStoryCommon;

namespace DeusaldStoryWeb;

/// <summary>
/// Blueprint authoring: create/rename/delete reusable Container/Logic/Function definitions (stored out-of-tree), place
/// and remove instances that reference them, turn an existing node into a blueprint, and reconcile every instance's
/// ports when a definition's boundary (or a function's signature) changes. All mutations funnel through the same
/// <see cref="Edit"/> + <see cref="MarkKeyDirty"/> choke point as the rest of the service, so history / save pick them up.
/// </summary>
public partial class ProjectStateService
{
    // ── Flattened (blueprint-expanded) project cache ───────────────────────────

    private StoryBlueprintExpander.ExpandResult? _ExpandCache;
    private int                                   _ExpandCacheVersion = -1;
    private int                                   _ExpandVersion;

    /// <summary>
    /// The transient project with every blueprint instance flattened into a per-instance clone of its definition, for
    /// runtime resolution (validation / preview). Cached and rebuilt only after the authoring project changes.
    /// </summary>
    public StoryBlueprintExpander.ExpandResult GetExpanded()
    {
        if (CurrentProject is null) return new StoryBlueprintExpander.ExpandResult();
        if (_ExpandCache is null || _ExpandCacheVersion != _ExpandVersion)
        {
            _ExpandCache        = StoryBlueprintExpander.Expand(CurrentProject);
            _ExpandCacheVersion = _ExpandVersion;
        }
        return _ExpandCache;
    }

    /// <summary>The flattened project only (see <see cref="GetExpanded"/>).</summary>
    public StoryProject GetExpandedProject() => GetExpanded().Project;

    // ── Blueprint descriptors (library) ────────────────────────────────────────

    /// <summary>Creates an empty Container blueprint — a bare out-of-tree container with one entry and one exit.</summary>
    public StoryBlueprint AddContainerBlueprint(string name)
    {
        using var _ = Edit();
        StoryContainerNode def = new() { Name = name, ParentContainer = Guid.Empty };
        def.EntryPoints.Add(new StoryConnectionPoint { Name = "In",  X = 40,  Y = 120 });
        def.ExitPoints.Add(new StoryConnectionPoint  { Name = "Out", X = 640, Y = 120 });
        CurrentProject!.ContainerNodes.Add(def.Id, def);

        StoryBlueprint bp = new() { Name = name, Kind = StoryBlueprintKind.Container, DefinitionNodeId = def.Id };
        CurrentProject.Blueprints.Add(bp.Id, bp);

        MarkKeyDirty(def.Id);
        MarkKeyDirty(bp.Id);
        return bp;
    }

    /// <summary>Creates an empty Logic blueprint — a bare out-of-tree logic node with one default choice.</summary>
    public StoryBlueprint AddLogicBlueprint(string name)
    {
        using var _ = Edit();
        StoryLogicNode def = new() { Name = name, ParentContainer = Guid.Empty };
        def.Choices.Add(new StoryChoice { Name = "Continue" });
        LayoutLogicInnerPoints(def);
        CurrentProject!.LogicNodes.Add(def.Id, def);

        StoryBlueprint bp = new() { Name = name, Kind = StoryBlueprintKind.Logic, DefinitionNodeId = def.Id };
        CurrentProject.Blueprints.Add(bp.Id, bp);

        MarkKeyDirty(def.Id);
        MarkKeyDirty(bp.Id);
        return bp;
    }

    /// <summary>Creates an empty Function blueprint — a bare out-of-tree logic node used as a computation body, no signature yet.</summary>
    public StoryBlueprint AddFunctionBlueprint(string name)
    {
        using var _ = Edit();
        StoryLogicNode def = new() { Name = name, ParentContainer = Guid.Empty };
        LayoutLogicInnerPoints(def);
        CurrentProject!.LogicNodes.Add(def.Id, def);

        StoryBlueprint bp = new() { Name = name, Kind = StoryBlueprintKind.Function, DefinitionNodeId = def.Id };
        CurrentProject.Blueprints.Add(bp.Id, bp);

        MarkKeyDirty(def.Id);
        MarkKeyDirty(bp.Id);
        return bp;
    }

    /// <summary>Renames / re-describes a blueprint.</summary>
    public void UpdateBlueprint(Guid blueprintId, string name, string description)
    {
        if (!CurrentProject!.Blueprints.TryGetValue(blueprintId, out StoryBlueprint? bp)) return;
        bp.Name        = name;
        bp.Description = description;
        MarkKeyDirty(blueprintId);
    }

    /// <summary>Number of live container-scope instances of a Container/Logic blueprint.</summary>
    public int BlueprintInstanceCount(Guid blueprintId) =>
        CurrentProject?.BlueprintInstances.Values.Count(i => i.BlueprintId == blueprintId) ?? 0;

    /// <summary>Number of live function instances of a Function blueprint (across every logic node's inner graph).</summary>
    public int FunctionInstanceCount(Guid blueprintId) =>
        CurrentProject?.LogicNodes.Values.Sum(l => l.FunctionInstanceNodes.Count(f => f.BlueprintId == blueprintId)) ?? 0;

    /// <summary>Deletes a blueprint together with every instance of it and its out-of-tree definition subtree.</summary>
    public void DeleteBlueprint(Guid blueprintId)
    {
        using var _ = Edit();
        if (!CurrentProject!.Blueprints.TryGetValue(blueprintId, out StoryBlueprint? bp)) return;

        if (bp.Kind == StoryBlueprintKind.Function)
        {
            foreach (StoryLogicNode logic in CurrentProject.LogicNodes.Values)
                foreach (StoryFunctionInstanceNode fi in logic.FunctionInstanceNodes.ToList())
                    if (fi.BlueprintId == blueprintId)
                        DeleteFunctionInstanceNode(logic.Id, fi.Id);
            CurrentProject.LogicNodes.Remove(bp.DefinitionNodeId);
            MarkKeyDirty(bp.DefinitionNodeId);
        }
        else
        {
            foreach (StoryBlueprintInstanceNode inst in CurrentProject.BlueprintInstances.Values.ToList())
                if (inst.BlueprintId == blueprintId)
                    DeleteBlueprintInstance(inst.ParentContainer, inst.Id);

            if (bp.Kind == StoryBlueprintKind.Container
             && CurrentProject.ContainerNodes.TryGetValue(bp.DefinitionNodeId, out StoryContainerNode? defContainer))
                DeleteContainerRecursive(defContainer);
            else
            {
                CurrentProject.LogicNodes.Remove(bp.DefinitionNodeId);
                MarkKeyDirty(bp.DefinitionNodeId);
            }
        }

        CurrentProject.Blueprints.Remove(blueprintId);
        MarkKeyDirty(blueprintId);
    }

    // ── Container-scope instances ──────────────────────────────────────────────

    /// <summary>Places a new instance of a Container/Logic blueprint in <paramref name="containerId"/>.</summary>
    public StoryBlueprintInstanceNode? AddBlueprintInstanceNode(Guid containerId, Guid blueprintId, double x, double y)
    {
        using var _ = Edit();
        if (!CurrentProject!.Blueprints.TryGetValue(blueprintId, out StoryBlueprint? bp)) return null;
        if (bp.Kind == StoryBlueprintKind.Function) return null; // function instances live inside a logic graph
        if (!CurrentProject.ContainerNodes.TryGetValue(containerId, out StoryContainerNode? container)) return null;

        StoryBlueprintInstanceNode inst = new() { BlueprintId = blueprintId, ParentContainer = containerId, X = x, Y = y };
        foreach (BlueprintBoundaryPort b in StoryBlueprintBoundary.Enumerate(CurrentProject, bp))
        {
            StoryBlueprintPortMap pm = new() { DefinitionPointId = b.DefinitionPointId, Name = b.Name };
            (b.IsEntry ? inst.EntryPorts : inst.ExitPorts).Add(pm);
        }

        CurrentProject.BlueprintInstances.Add(inst.Id, inst);
        container.Instances.Add(inst.Id);
        MarkKeyDirty(inst.Id);
        MarkKeyDirty(containerId);
        return inst;
    }

    /// <summary>Deletes a container-scope blueprint instance and the wires that touch its ports.</summary>
    public void DeleteBlueprintInstance(Guid containerId, Guid instanceId)
    {
        using var _ = Edit();
        if (!CurrentProject!.BlueprintInstances.TryGetValue(instanceId, out StoryBlueprintInstanceNode? inst)) return;

        List<Guid> pointIds = [.. inst.EntryPorts.Select(p => p.Id), .. inst.ExitPorts.Select(p => p.Id)];

        CurrentProject.BlueprintInstances.Remove(instanceId);
        if (CurrentProject.ContainerNodes.TryGetValue(containerId, out StoryContainerNode? container))
            container.Instances.Remove(instanceId);

        RemoveConnectionsFor(containerId, pointIds);
        MarkKeyDirty(instanceId);
        MarkKeyDirty(containerId);
    }

    // ── Function instances (inside a logic node's inner graph) ─────────────────

    /// <summary>Places a new instance of a Function blueprint on <paramref name="logicId"/>'s inner LFlow chain.</summary>
    public StoryFunctionInstanceNode? AddFunctionInstanceNode(Guid logicId, Guid blueprintId, double x, double y)
    {
        using var _ = Edit();
        if (!CurrentProject!.Blueprints.TryGetValue(blueprintId, out StoryBlueprint? bp) || bp.Kind != StoryBlueprintKind.Function) return null;
        if (!CurrentProject.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StoryFunctionInstanceNode inst = new() { BlueprintId = blueprintId, X = x, Y = y };
        foreach (StorySignaturePort input in bp.Inputs)
            inst.InputPorts.Add(new StoryBlueprintPortMap { DefinitionPointId = input.Id, Name = input.Name });
        foreach (StorySignaturePort output in bp.Outputs)
            inst.OutputPorts.Add(new StoryBlueprintPortMap { DefinitionPointId = output.Id, Name = output.Name });

        logic.FunctionInstanceNodes.Add(inst);
        MarkKeyDirty(logicId);
        return inst;
    }

    /// <summary>Deletes a function instance and the inner wires that touch its LFlow / signature ports.</summary>
    public void DeleteFunctionInstanceNode(Guid logicId, Guid nodeId)
    {
        using var _ = Edit();
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryFunctionInstanceNode? fi = logic.FunctionInstanceNodes.Find(n => n.Id == nodeId);
        if (fi is null) return;

        HashSet<Guid> pointIds = [fi.FlowIn.Id, fi.FlowOut.Id, .. fi.InputPorts.Select(p => p.Id), .. fi.OutputPorts.Select(p => p.Id)];
        logic.FunctionInstanceNodes.Remove(fi);
        logic.ContentConnections.RemoveAll(c => pointIds.Contains(c.FromPoint) || pointIds.Contains(c.ToPoint));
        MarkKeyDirty(logicId);
    }

    // ── Make into blueprint ────────────────────────────────────────────────────

    /// <summary>
    /// Turns an existing logic or container node into a new blueprint: moves the node out-of-tree as the shared
    /// definition and replaces it in place with an instance (existing wires preserved). Returns the new blueprint.
    /// </summary>
    public StoryBlueprint? MakeIntoBlueprint(Guid containerId, Guid nodeId)
    {
        using var _ = Edit();
        if (!CurrentProject!.ContainerNodes.TryGetValue(containerId, out StoryContainerNode? container)) return null;

        StoryBlueprintKind kind;
        double             x, y;
        if (CurrentProject.LogicNodes.TryGetValue(nodeId, out StoryLogicNode? logic) && container.Logic.Contains(nodeId))
        {
            kind = StoryBlueprintKind.Logic;
            x = logic.X; y = logic.Y;
            logic.ParentContainer = Guid.Empty;
            container.Logic.Remove(nodeId);
        }
        else if (CurrentProject.ContainerNodes.TryGetValue(nodeId, out StoryContainerNode? child) && container.Containers.Contains(nodeId))
        {
            kind = StoryBlueprintKind.Container;
            x = child.X; y = child.Y;
            child.ParentContainer = Guid.Empty;
            container.Containers.Remove(nodeId);
        }
        else return null;

        StoryBlueprint bp = new() { Name = NodeName(nodeId), Kind = kind, DefinitionNodeId = nodeId };
        CurrentProject.Blueprints.Add(bp.Id, bp);

        StoryBlueprintInstanceNode inst = new() { BlueprintId = bp.Id, ParentContainer = containerId, X = x, Y = y };
        Dictionary<Guid, Guid> rewire = new(); // definition boundary id -> instance port id
        foreach (BlueprintBoundaryPort b in StoryBlueprintBoundary.Enumerate(CurrentProject, bp))
        {
            StoryBlueprintPortMap pm = new() { DefinitionPointId = b.DefinitionPointId, Name = b.Name };
            (b.IsEntry ? inst.EntryPorts : inst.ExitPorts).Add(pm);
            rewire[b.DefinitionPointId] = pm.Id;
        }

        // Preserve the node's existing wires by repointing the definition's boundary ids to the new instance ports.
        foreach (StoryConnection c in container.Connections)
        {
            if (rewire.TryGetValue(c.FromPoint, out Guid nf)) c.FromPoint = nf;
            if (rewire.TryGetValue(c.ToPoint,   out Guid nt)) c.ToPoint   = nt;
        }

        CurrentProject.BlueprintInstances.Add(inst.Id, inst);
        container.Instances.Add(inst.Id);

        MarkKeyDirty(nodeId);
        MarkKeyDirty(inst.Id);
        MarkKeyDirty(containerId);
        MarkKeyDirty(bp.Id);
        return bp;
    }

    // ── Signature editing + reconciliation ─────────────────────────────────────

    /// <summary>Replaces a Function blueprint's signature and reconciles every instance's ports (dropping wires on removed / retyped ports).</summary>
    public void UpdateBlueprintSignature(
        Guid                                              blueprintId,
        IReadOnlyList<(Guid Id, string Name, PortType Type)> inputs,
        IReadOnlyList<(Guid Id, string Name, PortType Type)> outputs)
    {
        using var _ = Edit();
        if (!CurrentProject!.Blueprints.TryGetValue(blueprintId, out StoryBlueprint? bp) || bp.Kind != StoryBlueprintKind.Function) return;

        // Signature ports whose type changed — their existing wires may now be type-incompatible, so drop them.
        HashSet<Guid> retyped = new();
        foreach ((Guid Id, string Name, PortType Type) sig in inputs.Concat(outputs))
        {
            StorySignaturePort? old = bp.Inputs.Concat(bp.Outputs).FirstOrDefault(p => p.Id == sig.Id);
            if (old is not null && old.Type != sig.Type) retyped.Add(sig.Id);
        }

        bp.Inputs.Clear();
        foreach ((Guid id, string name, PortType type) in inputs)
            bp.Inputs.Add(new StorySignaturePort { Id = id == Guid.Empty ? Guid.NewGuid() : id, Name = name, Type = type });
        bp.Outputs.Clear();
        foreach ((Guid id, string name, PortType type) in outputs)
            bp.Outputs.Add(new StorySignaturePort { Id = id == Guid.Empty ? Guid.NewGuid() : id, Name = name, Type = type });

        MarkKeyDirty(blueprintId);
        ReconcileFunctionInstances(bp, retyped);
    }

    /// <summary>Re-syncs every instance of a Container/Logic blueprint to the definition's current boundary (add / drop ports + wires).</summary>
    public void ReconcileInstances(Guid blueprintId)
    {
        if (!CurrentProject!.Blueprints.TryGetValue(blueprintId, out StoryBlueprint? bp)) return;
        if (bp.Kind == StoryBlueprintKind.Function) { ReconcileFunctionInstances(bp, new HashSet<Guid>()); return; }

        List<BlueprintBoundaryPort> boundary = StoryBlueprintBoundary.Enumerate(CurrentProject, bp);
        List<(Guid DefId, string Name)> entries = boundary.Where(b => b.IsEntry).Select(b => (b.DefinitionPointId, b.Name)).ToList();
        List<(Guid DefId, string Name)> exits   = boundary.Where(b => !b.IsEntry).Select(b => (b.DefinitionPointId, b.Name)).ToList();

        foreach (StoryBlueprintInstanceNode inst in CurrentProject.BlueprintInstances.Values.Where(i => i.BlueprintId == blueprintId).ToList())
        {
            List<Guid> dropped = [.. ReconcilePortMaps(inst.EntryPorts, entries), .. ReconcilePortMaps(inst.ExitPorts, exits)];
            if (dropped.Count > 0) RemoveConnectionsFor(inst.ParentContainer, dropped);
            MarkKeyDirty(inst.Id);
        }
    }

    private void ReconcileFunctionInstances(StoryBlueprint bp, HashSet<Guid> retypedSignatureIds)
    {
        List<(Guid DefId, string Name)> inputs  = bp.Inputs.Select(p => (p.Id, p.Name)).ToList();
        List<(Guid DefId, string Name)> outputs = bp.Outputs.Select(p => (p.Id, p.Name)).ToList();

        foreach (StoryLogicNode logic in CurrentProject!.LogicNodes.Values)
        {
            bool touched = false;
            foreach (StoryFunctionInstanceNode fi in logic.FunctionInstanceNodes.Where(f => f.BlueprintId == bp.Id))
            {
                List<Guid> dead = [.. ReconcilePortMaps(fi.InputPorts, inputs), .. ReconcilePortMaps(fi.OutputPorts, outputs)];
                // A retyped port keeps its DefinitionPointId (so it isn't "dead"), but its old wires may be incompatible — drop them.
                dead.AddRange(fi.InputPorts.Concat(fi.OutputPorts).Where(p => retypedSignatureIds.Contains(p.DefinitionPointId)).Select(p => p.Id));
                if (dead.Count > 0)
                {
                    logic.ContentConnections.RemoveAll(c => dead.Contains(c.FromPoint) || dead.Contains(c.ToPoint));
                    touched = true;
                }
            }
            if (touched) MarkKeyDirty(logic.Id);
        }
    }

    /// <summary>Reconciles a port-map list in place against desired (DefId, Name) rows; returns the ids of dropped ports.</summary>
    private static List<Guid> ReconcilePortMaps(List<StoryBlueprintPortMap> ports, IReadOnlyList<(Guid DefId, string Name)> desired)
    {
        List<StoryBlueprintPortMap> rebuilt = new();
        foreach ((Guid defId, string name) in desired)
        {
            StoryBlueprintPortMap? existing = ports.Find(p => p.DefinitionPointId == defId);
            if (existing is not null) { existing.Name = name; rebuilt.Add(existing); }
            else                        rebuilt.Add(new StoryBlueprintPortMap { DefinitionPointId = defId, Name = name });
        }
        List<Guid> dropped = ports.Where(p => !rebuilt.Contains(p)).Select(p => p.Id).ToList();
        ports.Clear();
        ports.AddRange(rebuilt);
        return dropped;
    }

    /// <summary>If <paramref name="nodeId"/> is a blueprint's definition body, reconcile every instance to the new boundary.</summary>
    private void ReconcileIfDefinition(Guid nodeId)
    {
        if (CurrentProject?.Blueprints.Values.FirstOrDefault(b => b.DefinitionNodeId == nodeId) is StoryBlueprint bp)
            ReconcileInstances(bp.Id);
    }

    /// <summary>The display name of a logic / container node id (for naming a blueprint made from it).</summary>
    private string NodeName(Guid nodeId)
    {
        if (CurrentProject!.LogicNodes.TryGetValue(nodeId, out StoryLogicNode? l))     return string.IsNullOrWhiteSpace(l.Name) ? "Blueprint" : l.Name;
        if (CurrentProject.ContainerNodes.TryGetValue(nodeId, out StoryContainerNode? c)) return string.IsNullOrWhiteSpace(c.Name) ? "Blueprint" : c.Name;
        return "Blueprint";
    }

    // ── Recursion guard (for paste into a blueprint definition) ────────────────

    /// <summary>
    /// The blueprint whose definition body the graph rooted at <paramref name="graphNodeId"/> belongs to (a container or
    /// logic node), or <c>null</c> when it belongs to the live story. Climbs the <c>ParentContainer</c> chain to the
    /// out-of-tree root, then matches that root to a blueprint's <see cref="StoryBlueprint.DefinitionNodeId"/>.
    /// </summary>
    private StoryBlueprint? FindOwningBlueprint(Guid graphNodeId)
    {
        Guid rootId = OutOfTreeRoot(graphNodeId);
        if (rootId == Guid.Empty || rootId == CurrentProject!.Metadata.RootStoryContainerNodeId) return null;
        return CurrentProject.Blueprints.Values.FirstOrDefault(b => b.DefinitionNodeId == rootId);
    }

    /// <summary>Climbs the <c>ParentContainer</c> chain from a logic / container node to the out-of-tree root (the node whose parent is empty), or empty if the id is unknown.</summary>
    private Guid OutOfTreeRoot(Guid nodeId)
    {
        Guid current = nodeId;
        for (int guard = 0; guard < 4096; ++guard)
        {
            Guid parent;
            if (CurrentProject!.LogicNodes.TryGetValue(current, out StoryLogicNode? l))          parent = l.ParentContainer;
            else if (CurrentProject.ContainerNodes.TryGetValue(current, out StoryContainerNode? c)) parent = c.ParentContainer;
            else return Guid.Empty;
            if (parent == Guid.Empty) return current;
            current = parent;
        }
        return Guid.Empty;
    }

    /// <summary>
    /// Whether blueprint <paramref name="xId"/> transitively contains blueprint <paramref name="dId"/> (including
    /// <c>x == d</c>) — i.e. placing an instance of X inside D's definition would make D contain itself. Walks the
    /// blueprint-instance / function-instance references reachable from X's definition; a visited set defends against any
    /// pre-existing cycle in the data.
    /// </summary>
    private bool BlueprintDependsOn(Guid xId, Guid dId) => DependsOn(xId, dId, new HashSet<Guid>());

    private bool DependsOn(Guid xId, Guid dId, HashSet<Guid> visited)
    {
        if (xId == dId) return true;
        if (!visited.Add(xId)) return false;
        if (!CurrentProject!.Blueprints.TryGetValue(xId, out StoryBlueprint? x)) return false;
        foreach (Guid childBpId in InstancedBlueprintIds(x))
            if (DependsOn(childBpId, dId, visited)) return true;
        return false;
    }

    /// <summary>Every blueprint id instanced directly inside <paramref name="bp"/>'s definition body (container instances + function instances, whole subtree for a container).</summary>
    private IEnumerable<Guid> InstancedBlueprintIds(StoryBlueprint bp)
    {
        List<Guid> ids = new();
        if (bp.Kind == StoryBlueprintKind.Container
            && CurrentProject!.ContainerNodes.TryGetValue(bp.DefinitionNodeId, out StoryContainerNode? root))
        {
            List<object> entities = new();
            CollectSubtreeEntities(root, entities);
            foreach (object e in entities)
            {
                if (e is StoryBlueprintInstanceNode inst) ids.Add(inst.BlueprintId);
                if (e is StoryLogicNode logic)
                    foreach (StoryFunctionInstanceNode fi in logic.FunctionInstanceNodes) ids.Add(fi.BlueprintId);
            }
        }
        else if (CurrentProject!.LogicNodes.TryGetValue(bp.DefinitionNodeId, out StoryLogicNode? defLogic))
            foreach (StoryFunctionInstanceNode fi in defLogic.FunctionInstanceNodes) ids.Add(fi.BlueprintId);
        return ids;
    }
}
