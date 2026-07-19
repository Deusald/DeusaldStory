using System.Collections;
using System.Reflection;
using DeusaldStoryCommon;
using Newtonsoft.Json;

namespace DeusaldStoryWeb;

/// <summary>
/// Copy / paste support: deep-clones story nodes with fresh identity ids so a pasted copy collides with nothing.
/// Cloning re-serialises an entity, mints a new Guid for every <b>owned</b> id it holds (its own <c>Id</c> and every
/// connection point / choice / declared-variable / inner-node / connection id reachable inside it) and textually
/// rewrites those ids in the JSON — so intrinsic internal wiring (a logic node's content graph, a container's whole
/// subtree) is preserved and remapped consistently. A group paste shares <b>one</b> id map across all copied entities,
/// so wires between copied nodes are re-created (<see cref="ReplicateWires"/> / the inner <c>ContentConnections</c>
/// pass) and a <b>reference</b> id pointing at another copied node repoints to that node's clone; references to
/// entities outside the copied set (selected localization key / image / variable, a non-copied register target, …)
/// are left pointing at the originals. Copied portal points attach to the same portal rather than cloning a new pair.
/// </summary>
public partial class ProjectStateService
{
    // ── Public paste entry points ────────────────────────────────────────────

    /// <summary>
    /// Pastes copies of <paramref name="sourceEdIds"/> (logic / container / portal / comment nodes, identified by the
    /// canvas node ids the editor selected) into <paramref name="containerId"/>, offset by
    /// (<paramref name="dx"/>, <paramref name="dy"/>). A container is deep-copied together with its whole nested subtree.
    /// Returns the new top-level node ids (for re-selection). One atomic, undoable step.
    /// </summary>
    public List<Guid> PasteContainerNodes(Guid containerId, IReadOnlyList<Guid> sourceEdIds, double dx, double dy)
    {
        if (!CurrentProject!.ContainerNodes.TryGetValue(containerId, out StoryContainerNode? target)) return new();

        using var _ = Edit();
        List<Guid>             pasted            = new();
        HashSet<object>        seen              = new();
        Dictionary<Guid, Guid> map               = new(); // old owned point/node id → its fresh clone id (shared across the whole paste)
        List<object>           clones            = new(); // non-portal entities to deep-clone
        Guid                   sourceContainerId = Guid.Empty;

        // If we're pasting into a blueprint's definition body, an instance of a blueprint that (transitively) contains
        // that same blueprint would make it contain itself — drop such instances instead of creating the cycle.
        StoryBlueprint? owning = FindOwningBlueprint(containerId);

        foreach (Guid sourceId in sourceEdIds)
        {
            // A portal point attaches to the *same* portal instead of cloning a whole new pair, so the copy stays wired
            // to the portal's shared out. A container portal is many-in / one-out: only an *in* (the many side) may be
            // duplicated; copying the single out is illegal and is skipped.
            if (FindPortalByPoint(sourceId) is StoryPortalNode portal)
            {
                if (portal.ParentContainer != containerId) continue; // the pair lives elsewhere — can't attach across containers
                StoryConnectionPoint? inPoint = portal.InPoints.Find(p => p.Id == sourceId);
                if (inPoint is null) continue;                       // the single out — nothing legal to paste
                StoryConnectionPoint newIn = new() { Name = "In", X = inPoint.X + dx, Y = inPoint.Y + dy };
                portal.InPoints.Add(newIn);
                map[inPoint.Id]  = newIn.Id;
                if (sourceContainerId == Guid.Empty) sourceContainerId = portal.ParentContainer;
                MarkKeyDirty(portal.Id);
                pasted.Add(newIn.Id);
                continue;
            }

            object? entity = ResolveContainerEntity(containerId, sourceId);
            if (entity is null || !seen.Add(entity)) continue;
            if (entity is StoryBlueprintInstanceNode bpInst
                && owning is not null && BlueprintDependsOn(bpInst.BlueprintId, owning.Id)) continue; // self-recursion
            clones.Add(entity);
            if (sourceContainerId == Guid.Empty) sourceContainerId = ParentContainerOf(entity);
        }

        // One shared id map over every clone entity, so wires between copied nodes (and references among them) remap
        // consistently. Portal-attach entries are already seeded above.
        HashSet<object> collectSeen = new();
        foreach (object e in clones) CollectOwnedIds(e, map, collectSeen);

        foreach (object entity in clones)
        {
            switch (entity)
            {
                case StoryLogicNode logic:
                {
                    StoryLogicNode clone = RemapClone(logic, map);
                    clone.ParentContainer = containerId;
                    clone.X += dx;
                    clone.Y += dy;
                    CurrentProject.LogicNodes.Add(clone.Id, clone);
                    target.Logic.Add(clone.Id);
                    MarkKeyDirty(clone.Id);
                    pasted.Add(clone.Id);
                    break;
                }
                case StoryCommentNode comment:
                {
                    StoryCommentNode clone = RemapClone(comment, map);
                    clone.X += dx;
                    clone.Y += dy;
                    target.Comments.Add(clone);
                    pasted.Add(clone.Id);
                    break;
                }
                case StoryContainerNode container:
                    pasted.Add(PasteContainerSubtree(container, containerId, target, dx, dy, map));
                    break;
                case StoryBlueprintInstanceNode inst:
                {
                    // Clone remaps the instance's own id + its port-map ids (owned); BlueprintId / DefinitionPointId are
                    // plain Guids that survive, so the copy still references the same blueprint definition.
                    StoryBlueprintInstanceNode clone = RemapClone(inst, map);
                    clone.ParentContainer = containerId;
                    clone.X += dx;
                    clone.Y += dy;
                    CurrentProject.BlueprintInstances.Add(clone.Id, clone);
                    target.Instances.Add(clone.Id);
                    MarkKeyDirty(clone.Id);
                    pasted.Add(clone.Id);
                    break;
                }
            }
        }

        ReplicateWires(sourceContainerId, target, map);

        if (pasted.Count > 0) MarkKeyDirty(containerId);
        return pasted;
    }

    /// <summary>The container a copyable top-level entity lives in (portals are handled separately), or empty for comments.</summary>
    private static Guid ParentContainerOf(object entity) => entity switch
    {
        StoryLogicNode             l => l.ParentContainer,
        StoryContainerNode         c => c.ParentContainer,
        StoryBlueprintInstanceNode i => i.ParentContainer,
        _                            => Guid.Empty
    };

    /// <summary>
    /// Re-creates, on <paramref name="target"/>, every wire of the source container whose <b>both</b> endpoints were
    /// copied (their ids appear in <paramref name="map"/>) — so a pasted sub-graph keeps its internal connections.
    /// Wires that touch a non-copied node (or a portal's shared side) are intentionally not duplicated.
    /// </summary>
    private void ReplicateWires(Guid sourceContainerId, StoryContainerNode target, IReadOnlyDictionary<Guid, Guid> map)
    {
        if (sourceContainerId == Guid.Empty
            || !CurrentProject!.ContainerNodes.TryGetValue(sourceContainerId, out StoryContainerNode? source)) return;

        List<StoryConnection> add = new(); // buffer: source may be the same list as target
        foreach (StoryConnection c in source.Connections)
            if (map.TryGetValue(c.FromPoint, out Guid from) && map.TryGetValue(c.ToPoint, out Guid to))
                add.Add(new StoryConnection { FromPoint = from, ToPoint = to });
        target.Connections.AddRange(add);
    }

    /// <summary>
    /// Pastes copies of <paramref name="sourceEdIds"/> (inner content nodes of a logic graph, identified by the canvas
    /// node ids the editor selected) into logic node <paramref name="logicId"/>, offset by
    /// (<paramref name="dx"/>, <paramref name="dy"/>). The copied ids are owned by <paramref name="sourceLogicId"/> — the
    /// logic node the copy was taken from — which may differ from the target (e.g. pasting a story logic node's inner
    /// graph into a Logic/Function blueprint definition). Wires between copied nodes are re-created; when source and
    /// target are the same node a copied logic-portal out attaches to the same portal. Returns the new canvas node ids.
    /// One step.
    /// </summary>
    public List<Guid> PasteLogicInnerNodes(Guid sourceLogicId, Guid logicId, IReadOnlyList<Guid> sourceEdIds, double dx, double dy)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return new();
        // Inner content nodes aren't project-global — they only resolve against their owning logic node. Resolve the
        // copied ids (and their wires) against the *source* node; the target is only where the clones land.
        if (!CurrentProject.LogicNodes.TryGetValue(sourceLogicId, out StoryLogicNode? source)) source = logic;
        bool sameNode = ReferenceEquals(source, logic);

        using var _ = Edit();
        List<Guid>             pasted  = new();
        HashSet<object>        seen    = new();
        Dictionary<Guid, Guid> map     = new(); // shared old→new id map across the whole paste
        List<object>           clones  = new();
        List<object>           created = new(); // the clones actually inserted, for a post-pass over cross-node references

        // Pasting into a blueprint's definition body: a function instance of a blueprint that (transitively) contains
        // that same blueprint would make it call itself — drop such instances instead of creating the cycle.
        StoryBlueprint? owning = FindOwningBlueprint(logicId);

        foreach (Guid sourceId in sourceEdIds)
        {
            // A logic portal is one-in / many-out: within the same node a copied *out* (the many side) attaches as
            // another out on the same portal, so it re-emits the same source; copying the single in is illegal. Across
            // nodes there is no shared portal to attach to, so the portal falls through to be cloned whole (below).
            if (sameNode && FindLogicPortalByPoint(logic, sourceId) is StoryLogicPortalNode portal)
            {
                StoryConnectionPoint? outPoint = portal.OutPoints.Find(p => p.Id == sourceId);
                if (outPoint is null) continue; // the single in — nothing legal to paste
                StoryConnectionPoint newOut = new() { Name = "Out", X = outPoint.X + dx, Y = outPoint.Y + dy };
                portal.OutPoints.Add(newOut);
                map[outPoint.Id] = newOut.Id;
                pasted.Add(newOut.Id);
                continue;
            }

            object? model = FindInnerModel(source, sourceId);
            if (model is null || !seen.Add(model)) continue;
            if (model is StoryFunctionInstanceNode fnInst
                && owning is not null && BlueprintDependsOn(fnInst.BlueprintId, owning.Id)) continue; // self-recursion
            clones.Add(model);
        }

        HashSet<object> collectSeen = new();
        foreach (object m in clones) CollectOwnedIds(m, map, collectSeen);

        foreach (object model in clones)
        {
            if (model is StoryConditionFlowNode cflow)
            {
                StoryConditionFlowNode clone = RemapClone(cflow, map);
                clone.EndId = Guid.NewGuid(); // the End card's id isn't an "Id" property, so mint it fresh to avoid a collision
                clone.X    += dx; clone.Y    += dy;
                clone.EndX += dx; clone.EndY += dy;
                logic.ConditionFlowNodes.Add(clone);
                pasted.Add(clone.Id);
                created.Add(clone);
                continue;
            }

            object cloneObj = RemapClone(model, map);
            OffsetXY(cloneObj, dx, dy);
            AddCloneToLogic(logic, cloneObj);
            if (cloneObj.GetType().GetProperty("Id")?.GetValue(cloneObj) is Guid id) pasted.Add(id);
            created.Add(cloneObj);
        }

        // Cross-node paste: a copied node's reference into the *source* graph (a Register node it reads, a variable it
        // compares) is only valid if that target was copied too — a reference to a node left behind now dangles in the
        // target graph, so blank it. RemapClone already repointed references whose target *was* copied to the clone.
        if (!sameNode)
        {
            HashSet<Guid> copiedIds = new(map.Values);
            foreach (object o in created) DropDanglingLocalRefs(o, copiedIds);
        }

        // Re-create every inner wire of the *source* whose both endpoints were copied (buffer first — source and target
        // may be the same list we mutate).
        List<StoryConnection> add = new();
        foreach (StoryConnection c in source.ContentConnections)
            if (map.TryGetValue(c.FromPoint, out Guid from) && map.TryGetValue(c.ToPoint, out Guid to))
                add.Add(new StoryConnection { FromPoint = from, ToPoint = to });
        logic.ContentConnections.AddRange(add);

        if (pasted.Count > 0) MarkKeyDirty(logicId);
        return pasted;
    }

    // ── Entity resolution ──────────────────────────────────────────────────────

    /// <summary>Resolves a container-graph canvas node id to the story entity it belongs to (logic / container / portal / comment), or null.</summary>
    private object? ResolveContainerEntity(Guid containerId, Guid edId)
    {
        if (CurrentProject!.LogicNodes.TryGetValue(edId, out StoryLogicNode? logic))       return logic;
        if (CurrentProject.ContainerNodes.TryGetValue(edId, out StoryContainerNode? child)) return child;
        if (CurrentProject.BlueprintInstances.TryGetValue(edId, out StoryBlueprintInstanceNode? inst)) return inst;
        if (FindPortalByPoint(edId) is StoryPortalNode portal)                              return portal;
        if (CurrentProject.ContainerNodes.TryGetValue(containerId, out StoryContainerNode? owner))
            return owner.Comments.Find(c => c.Id == edId);
        return null;
    }

    /// <summary>Resolves an inner-graph canvas node id to the content model it belongs to (a logic portal resolves from its in/out points), or null for a non-copyable boundary node.</summary>
    private object? FindInnerModel(StoryLogicNode logic, Guid edId)
    {
        object? found =
            (object?)logic.LocalizationNodes.Find(n => n.Id == edId)
            ?? logic.IconNodes.Find(n => n.Id == edId)
            ?? logic.LightDarkSwitchNodes.Find(n => n.Id == edId)
            ?? logic.SmartFormatNodes.Find(n => n.Id == edId)
            ?? logic.GetVariableNodes.Find(n => n.Id == edId)
            ?? logic.ConstantVariableNodes.Find(n => n.Id == edId)
            ?? logic.ConstantStringNodes.Find(n => n.Id == edId)
            ?? logic.RandomizedInstructionNodes.Find(n => n.Id == edId)
            ?? logic.FlowTextNodes.Find(n => n.Id == edId)
            ?? logic.SplitForAppNodes.Find(n => n.Id == edId)
            ?? logic.SetVariableNodes.Find(n => n.Id == edId)
            ?? logic.ConditionFlowNodes.Find(n => n.Id == edId || n.EndId == edId)
            ?? (object?)logic.FunctionInstanceNodes.Find(n => n.Id == edId)
            ?? logic.CommentNodes.Find(n => n.Id == edId);
        return found ?? FindLogicPortalByPoint(logic, edId);
    }

    /// <summary>
    /// Deep-clones a container together with every entity nested beneath it, registering the clones and returning the
    /// new top-level container id. Uses the caller's shared <paramref name="map"/> so the container's boundary points
    /// keep the same fresh ids the top-level wire replication expects; the subtree's own descendant ids are added to it.
    /// </summary>
    private Guid PasteContainerSubtree(StoryContainerNode source, Guid parentId, StoryContainerNode parent, double dx, double dy, Dictionary<Guid, Guid> map)
    {
        List<object> entities = new();
        CollectSubtreeEntities(source, entities);

        foreach (object e in entities) CollectOwnedIds(e, map, new HashSet<object>());

        Guid newTopId = Guid.Empty;
        foreach (object e in entities)
        {
            switch (e)
            {
                case StoryContainerNode c:
                {
                    StoryContainerNode clone = RemapClone(c, map);
                    if (c.Id == source.Id)
                    {
                        clone.ParentContainer = parentId;
                        clone.X += dx;
                        clone.Y += dy;
                        parent.Containers.Add(clone.Id);
                        newTopId = clone.Id;
                    }
                    CurrentProject!.ContainerNodes[clone.Id] = clone;
                    MarkKeyDirty(clone.Id);
                    break;
                }
                case StoryLogicNode l:
                {
                    StoryLogicNode clone = RemapClone(l, map);
                    CurrentProject!.LogicNodes[clone.Id] = clone;
                    MarkKeyDirty(clone.Id);
                    break;
                }
                case StoryPortalNode po:
                {
                    StoryPortalNode clone = RemapClone(po, map);
                    CurrentProject!.PortalNodes[clone.Id] = clone;
                    MarkKeyDirty(clone.Id);
                    break;
                }
                case StoryBlueprintInstanceNode inst:
                {
                    StoryBlueprintInstanceNode clone = RemapClone(inst, map);
                    CurrentProject!.BlueprintInstances[clone.Id] = clone;
                    MarkKeyDirty(clone.Id);
                    break;
                }
            }
        }
        return newTopId;
    }

    /// <summary>Gathers a container and all its descendant logic / portal / container entities (depth-first) into <paramref name="entities"/>.</summary>
    private void CollectSubtreeEntities(StoryContainerNode container, List<object> entities)
    {
        entities.Add(container);
        foreach (Guid logicId in container.Logic)
            if (CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) entities.Add(logic);
        foreach (Guid portalId in container.Portals)
            if (CurrentProject!.PortalNodes.TryGetValue(portalId, out StoryPortalNode? portal)) entities.Add(portal);
        foreach (Guid instanceId in container.Instances)
            if (CurrentProject!.BlueprintInstances.TryGetValue(instanceId, out StoryBlueprintInstanceNode? inst)) entities.Add(inst);
        foreach (Guid childId in container.Containers)
            if (CurrentProject!.ContainerNodes.TryGetValue(childId, out StoryContainerNode? child)) CollectSubtreeEntities(child, entities);
    }

    // ── Cloning primitives ─────────────────────────────────────────────────────

    /// <summary>Serialises <paramref name="entity"/> and rewrites every mapped id, returning a fresh deserialized copy.</summary>
    private static T RemapClone<T>(T entity, IReadOnlyDictionary<Guid, Guid> map) where T : notnull
    {
        string json = JsonConvert.SerializeObject(entity, _HistoryJson);
        foreach (KeyValuePair<Guid, Guid> kv in map)
            json = json.Replace(kv.Key.ToString(), kv.Value.ToString());
        // Deserialize to the entity's runtime type, not T: inner-node paste clones through a static `object`, and
        // JsonConvert.DeserializeObject<object> would hand back a JObject instead of the concrete node.
        return (T)JsonConvert.DeserializeObject(json, entity.GetType(), _HistoryJson)!;
    }

    /// <summary>
    /// Walks the object graph of a story entity and records a fresh Guid for every <b>owned</b> identity id — the
    /// <c>Id</c> property of every reachable model object (nodes, connection points, choices, declared variables,
    /// connections). Reference ids (plain Guid fields that point at other entities) are skipped, so they survive a clone.
    /// </summary>
    private static void CollectOwnedIds(object? obj, IDictionary<Guid, Guid> map, HashSet<object> seen)
    {
        if (obj is null) return;
        Type t = obj.GetType();
        if (t.IsValueType || obj is string) return;                      // skip Guids/enums/primitives and strings
        if (t.Namespace is null || !t.Namespace.StartsWith("DeusaldStoryCommon")) return; // only our model objects
        if (!seen.Add(obj)) return;

        if (t.GetProperty("Id") is { } idProp && idProp.PropertyType == typeof(Guid)
            && idProp.GetValue(obj) is Guid id && id != Guid.Empty && !map.ContainsKey(id))
            map[id] = Guid.NewGuid();

        foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            object? val = p.GetValue(obj);
            if (val is null) continue;
            if (val is IEnumerable en and not string)
                foreach (object? item in en) CollectOwnedIds(item, map, seen);
            else
                CollectOwnedIds(val, map, seen);
        }
    }

    /// <summary>Adds <paramref name="dx"/>/<paramref name="dy"/> to an object's X/Y canvas position when it has one.</summary>
    private static void OffsetXY(object o, double dx, double dy)
    {
        if (o.GetType().GetProperty("X") is { CanWrite: true } xp && xp.PropertyType == typeof(double))
            xp.SetValue(o, (double)xp.GetValue(o)! + dx);
        if (o.GetType().GetProperty("Y") is { CanWrite: true } yp && yp.PropertyType == typeof(double))
            yp.SetValue(o, (double)yp.GetValue(o)! + dy);
    }

    /// <summary>
    /// Blanks any graph-local reference on a cloned inner node whose target wasn't part of the copy (its remapped id is
    /// not in <paramref name="copiedIds"/>) — used only for a cross-node paste, where such a reference would otherwise
    /// dangle into the source graph. Project-global references (localization keys, images, story variables, blueprint
    /// ids) are left alone: they resolve the same in any graph.
    /// </summary>
    private static void DropDanglingLocalRefs(object node, HashSet<Guid> copiedIds)
    {
        switch (node)
        {
            // Get/Set target a project-global variable id (like a localization key), so it needs no blanking.
            case StorySetVariableNode s:   SanitizeCondition(s.ValidationRule, copiedIds); break;
            case StoryConditionFlowNode c: SanitizeCondition(c.Condition, copiedIds);      break;
        }
    }

    /// <summary>
    /// Recursively blanks a condition tree's variable operand refs that point at a non-copied node. The
    /// <see cref="StorageValidation.ThisEntryRef"/> sentinel (a well-known constant, not a node id) is preserved.
    /// </summary>
    private static void SanitizeCondition(StoryConditionExpr? expr, HashSet<Guid> copiedIds)
    {
        if (expr is null) return;
        if (expr.LeftVariableRef != Guid.Empty && expr.LeftVariableRef != StorageValidation.ThisEntryRef
            && !copiedIds.Contains(expr.LeftVariableRef)) expr.LeftVariableRef = Guid.Empty;
        if (expr.RightKind == StoryConditionOperandKind.Variable && expr.RightVariableRef != Guid.Empty
            && expr.RightVariableRef != StorageValidation.ThisEntryRef && !copiedIds.Contains(expr.RightVariableRef))
            expr.RightVariableRef = Guid.Empty;
        foreach (StoryConditionExpr child in expr.Children) SanitizeCondition(child, copiedIds);
    }

    /// <summary>Appends a cloned inner-content node to the matching <c>List&lt;T&gt;</c> on <paramref name="logic"/> by its runtime type.</summary>
    private static void AddCloneToLogic(StoryLogicNode logic, object clone)
    {
        Type cloneType = clone.GetType();
        foreach (PropertyInfo p in typeof(StoryLogicNode).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.PropertyType.IsGenericType || !typeof(IEnumerable).IsAssignableFrom(p.PropertyType)) continue;
            if (p.PropertyType.GetGenericArguments()[0] != cloneType) continue;
            object? list = p.GetValue(logic);
            list?.GetType().GetMethod("Add")?.Invoke(list, new[] { clone });
            return;
        }
    }
}
