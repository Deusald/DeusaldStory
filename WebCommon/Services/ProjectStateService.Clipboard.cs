using System.Collections;
using System.Reflection;
using DeusaldStoryCommon;
using Newtonsoft.Json;

namespace DeusaldStoryWeb;

/// <summary>
/// Copy / paste support: deep-clones story nodes with fresh identity ids so a pasted copy collides with nothing and
/// carries no wires to the surrounding graph. Cloning re-serialises an entity, mints a new Guid for every <b>owned</b>
/// id it holds (its own <c>Id</c> and every connection point / choice / declared-variable / inner-node / connection id
/// reachable inside it) and textually rewrites those ids in the JSON — so intrinsic internal wiring (a logic node's
/// content graph, a container's whole subtree) is preserved and remapped consistently, while <b>reference</b> ids
/// (selected localization key / image / variable, registered-variable target, …) are left pointing at the originals.
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
        List<Guid>      pasted = new();
        HashSet<object> seen   = new();

        foreach (Guid sourceId in sourceEdIds)
        {
            object? entity = ResolveContainerEntity(containerId, sourceId);
            if (entity is null || !seen.Add(entity)) continue;

            switch (entity)
            {
                case StoryLogicNode logic:
                {
                    StoryLogicNode clone = CloneEntity(logic);
                    clone.ParentContainer = containerId;
                    clone.X += dx;
                    clone.Y += dy;
                    CurrentProject.LogicNodes.Add(clone.Id, clone);
                    target.Logic.Add(clone.Id);
                    MarkKeyDirty(clone.Id);
                    pasted.Add(clone.Id);
                    break;
                }
                case StoryPortalNode portal:
                {
                    StoryPortalNode clone = CloneEntity(portal);
                    clone.ParentContainer = containerId;
                    clone.OutPoint.X += dx;
                    clone.OutPoint.Y += dy;
                    foreach (StoryConnectionPoint ip in clone.InPoints) { ip.X += dx; ip.Y += dy; }
                    CurrentProject.PortalNodes.Add(clone.Id, clone);
                    target.Portals.Add(clone.Id);
                    MarkKeyDirty(clone.Id);
                    pasted.Add(clone.InPoints.Count > 0 ? clone.InPoints[0].Id : clone.OutPoint.Id);
                    break;
                }
                case StoryCommentNode comment:
                {
                    StoryCommentNode clone = CloneEntity(comment);
                    clone.X += dx;
                    clone.Y += dy;
                    target.Comments.Add(clone);
                    pasted.Add(clone.Id);
                    break;
                }
                case StoryContainerNode container:
                    pasted.Add(PasteContainerSubtree(container, containerId, target, dx, dy));
                    break;
            }
        }

        if (pasted.Count > 0) MarkKeyDirty(containerId);
        return pasted;
    }

    /// <summary>
    /// Pastes copies of <paramref name="sourceEdIds"/> (inner content nodes of a logic graph, identified by the canvas
    /// node ids the editor selected) into logic node <paramref name="logicId"/>, offset by
    /// (<paramref name="dx"/>, <paramref name="dy"/>). Copies carry no wires. Returns the new canvas node ids. One step.
    /// </summary>
    public List<Guid> PasteLogicInnerNodes(Guid logicId, IReadOnlyList<Guid> sourceEdIds, double dx, double dy)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return new();

        using var _ = Edit();
        List<Guid>      pasted = new();
        HashSet<object> seen   = new();

        foreach (Guid sourceId in sourceEdIds)
        {
            object? model = FindInnerModel(logic, sourceId);
            if (model is null || !seen.Add(model)) continue;

            if (model is StoryLogicPortalNode portal)
            {
                StoryLogicPortalNode clone = CloneEntity(portal);
                clone.InPoint.X += dx;
                clone.InPoint.Y += dy;
                foreach (StoryConnectionPoint o in clone.OutPoints) { o.X += dx; o.Y += dy; }
                logic.LogicPortalNodes.Add(clone);
                pasted.Add(clone.InPoint.Id);
                continue;
            }

            object cloneObj = CloneEntity(model);
            OffsetXY(cloneObj, dx, dy);
            AddCloneToLogic(logic, cloneObj);
            if (cloneObj.GetType().GetProperty("Id")?.GetValue(cloneObj) is Guid id) pasted.Add(id);
        }

        if (pasted.Count > 0) MarkKeyDirty(logicId);
        return pasted;
    }

    // ── Entity resolution ──────────────────────────────────────────────────────

    /// <summary>Resolves a container-graph canvas node id to the story entity it belongs to (logic / container / portal / comment), or null.</summary>
    private object? ResolveContainerEntity(Guid containerId, Guid edId)
    {
        if (CurrentProject!.LogicNodes.TryGetValue(edId, out StoryLogicNode? logic))       return logic;
        if (CurrentProject.ContainerNodes.TryGetValue(edId, out StoryContainerNode? child)) return child;
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
            ?? logic.ExternalVariableNodes.Find(n => n.Id == edId)
            ?? logic.GetVariableNodes.Find(n => n.Id == edId)
            ?? logic.ConstantVariableNodes.Find(n => n.Id == edId)
            ?? logic.FlowTextNodes.Find(n => n.Id == edId)
            ?? logic.SplitForAppNodes.Find(n => n.Id == edId)
            ?? logic.RegisterVariableNodes.Find(n => n.Id == edId)
            ?? logic.SetVariableNodes.Find(n => n.Id == edId)
            ?? logic.UnregisterVariableNodes.Find(n => n.Id == edId)
            ?? logic.SetExternalVariableNodes.Find(n => n.Id == edId)
            ?? (object?)logic.CommentNodes.Find(n => n.Id == edId);
        return found ?? FindLogicPortalByPoint(logic, edId);
    }

    /// <summary>Deep-clones a container together with every entity nested beneath it, registering the clones and returning the new top-level container id.</summary>
    private Guid PasteContainerSubtree(StoryContainerNode source, Guid parentId, StoryContainerNode parent, double dx, double dy)
    {
        List<object> entities = new();
        CollectSubtreeEntities(source, entities);

        Dictionary<Guid, Guid> map = new();
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
        foreach (Guid childId in container.Containers)
            if (CurrentProject!.ContainerNodes.TryGetValue(childId, out StoryContainerNode? child)) CollectSubtreeEntities(child, entities);
    }

    // ── Cloning primitives ─────────────────────────────────────────────────────

    /// <summary>Clones a single entity with all its owned ids freshly minted (references left intact).</summary>
    private static T CloneEntity<T>(T entity) where T : notnull
    {
        Dictionary<Guid, Guid> map = new();
        CollectOwnedIds(entity, map, new HashSet<object>());
        return RemapClone(entity, map);
    }

    /// <summary>Serialises <paramref name="entity"/> and rewrites every mapped id, returning a fresh deserialized copy.</summary>
    private static T RemapClone<T>(T entity, IReadOnlyDictionary<Guid, Guid> map)
    {
        string json = JsonConvert.SerializeObject(entity, _HistoryJson);
        foreach (KeyValuePair<Guid, Guid> kv in map)
            json = json.Replace(kv.Key.ToString(), kv.Value.ToString());
        return JsonConvert.DeserializeObject<T>(json, _HistoryJson)!;
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
