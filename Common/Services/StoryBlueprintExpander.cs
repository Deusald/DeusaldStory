using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Turns an authoring <see cref="StoryProject"/> (blueprints stored as references — one shared definition + thin
    /// instances) into a <b>transient, flattened</b> project where every blueprint instance has been replaced by a
    /// deterministic per-instance deep clone of its definition. Every existing runtime service
    /// (<see cref="StoryFlowNavigator"/>, <see cref="StoryGraphValidator"/>, <see cref="StoryGamebookPreview"/>,
    /// <see cref="StoryLogicRenderer"/>) then runs unchanged on the expanded project — it contains no instances and no
    /// out-of-tree definitions, so nothing special-cases them.
    ///
    /// Two passes:
    /// <list type="number">
    /// <item><b>Container/Logic</b> — walking from the root, splice each container-scope instance's definition subtree
    /// into the container graph.</item>
    /// <item><b>Function</b> — over every resulting logic node, inline each function instance's inner graph into the
    /// node's LFlow spine.</item>
    /// </list>
    /// Ids are remapped with a deterministic combine (not random) so expansion is stable across re-runs.
    /// </summary>
    [PublicAPI]
    public static class StoryBlueprintExpander
    {
        private const int _DEPTH_CAP = 256;

        private static readonly JsonSerializerSettings _Json = new()
        {
            Formatting        = Formatting.None,
            NullValueHandling = NullValueHandling.Include,
            Converters        = { new StringEnumConverter() },
        };

        public sealed class ExpandResult
        {
            public StoryProject                                     Project  { get; set; } = new();
            /// <summary>Flattened node id → the (instance, definition) it originated from, for problem de-duplication.</summary>
            public Dictionary<Guid, (Guid InstanceId, Guid DefinitionNodeId)> Origin { get; } = new();
            /// <summary>Structural problems found while expanding (missing / recursive blueprints).</summary>
            public List<StoryProblem>                               Problems { get; } = new();
        }

        // ── Entry point ──────────────────────────────────────────────────────────

        public static ExpandResult Expand(StoryProject authoring)
        {
            ExpandResult result = new()
            {
                Project = new StoryProject { Metadata = authoring.Metadata }
            };
            StoryProject outProj = result.Project;

            // Variables/Images are never mutated by expansion — share references.
            foreach (KeyValuePair<Guid, StoryVariable> kv in authoring.Variables) outProj.Variables[kv.Key] = kv.Value;
            foreach (KeyValuePair<Guid, StoryImage>    kv in authoring.Images)    outProj.Images[kv.Key]    = kv.Value;

            // Pass 0: deep-copy every node reachable from the root (definitions have ParentContainer == Empty and are
            // in no child list, so they are never reached — the flattened project has no raw definitions).
            Guid rootId = authoring.Metadata.RootStoryContainerNodeId;
            HashSet<Guid> copied = new();
            CopyReachable(authoring, outProj, rootId, copied);

            // Pass 1: expand container-scope instances (recursively, so nested container-blueprints resolve too).
            foreach (StoryContainerNode container in outProj.ContainerNodes.Values.ToList())
                foreach (Guid instId in container.Instances.ToList())
                    if (outProj.BlueprintInstances.TryGetValue(instId, out StoryBlueprintInstanceNode? inst))
                        ExpandContainerInstance(authoring, outProj, inst, container, new HashSet<Guid>(), 0, result);

            // Pass 2: inline function instances into every logic node's inner graph.
            foreach (StoryLogicNode logic in outProj.LogicNodes.Values.ToList())
                foreach (StoryFunctionInstanceNode fi in logic.FunctionInstanceNodes.ToList())
                    InlineFunctionInstance(authoring, logic, fi, new HashSet<Guid>(), 0, result);

            return result;
        }

        // ── Pass 0: copy the reachable tree ───────────────────────────────────────

        private static void CopyReachable(StoryProject src, StoryProject dst, Guid containerId, HashSet<Guid> copied)
        {
            if (containerId == Guid.Empty || !copied.Add(containerId)) return;
            if (!src.ContainerNodes.TryGetValue(containerId, out StoryContainerNode? container)) return;

            dst.ContainerNodes[containerId] = DeepClone(container);

            foreach (Guid logicId in container.Logic)
                if (src.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic))
                    dst.LogicNodes[logicId] = DeepClone(logic);
            foreach (Guid portalId in container.Portals)
                if (src.PortalNodes.TryGetValue(portalId, out StoryPortalNode? portal))
                    dst.PortalNodes[portalId] = DeepClone(portal);
            foreach (Guid instId in container.Instances)
                if (src.BlueprintInstances.TryGetValue(instId, out StoryBlueprintInstanceNode? inst))
                    dst.BlueprintInstances[instId] = DeepClone(inst);
            foreach (Guid childId in container.Containers)
                CopyReachable(src, dst, childId, copied);
        }

        // ── Pass 1: container / logic instances ───────────────────────────────────

        private static void ExpandContainerInstance(
            StoryProject               authoring,
            StoryProject               outProj,
            StoryBlueprintInstanceNode inst,
            StoryContainerNode         outParent,
            HashSet<Guid>              stack,
            int                        depth,
            ExpandResult               result)
        {
            // Always drop the instance node itself from the flattened project.
            outProj.BlueprintInstances.Remove(inst.Id);
            outParent.Instances.Remove(inst.Id);

            if (depth > _DEPTH_CAP) { AddProblem(result, outParent.Id, UiLang.T(Localization.Validation.blueprintNestingTooDeep)); return; }
            if (!authoring.Blueprints.TryGetValue(inst.BlueprintId, out StoryBlueprint? bp))
            {
                AddProblem(result, outParent.Id, UiLang.T(Localization.Validation.blueprintMissing));
                return;
            }
            if (!stack.Add(bp.Id))
            {
                AddProblem(result, outParent.Id, UiLang.T(Localization.Validation.blueprintRecursive, new Dictionary<string, object> { ["name"] = bp.Name }));
                return;
            }

            // Map every owned id in the definition subtree to a deterministic per-instance id; override the boundary
            // points so the definition's internal wiring meets the parent container's existing wires at the instance ports.
            List<object> entities = CollectDefinitionEntities(authoring, bp);
            Dictionary<Guid, Guid> map = new();
            foreach (object e in entities) CollectOwnedIds(e, id => Combine(inst.Id, id), map);
            foreach (StoryBlueprintPortMap pm in inst.EntryPorts.Concat(inst.ExitPorts))
                map[pm.DefinitionPointId] = pm.Id;

            List<StoryBlueprintInstanceNode> nestedInstances = new();

            foreach (object e in entities)
            {
                switch (e)
                {
                    case StoryLogicNode l:
                    {
                        StoryLogicNode clone = RemapClone(l, map);
                        if (bp.Kind == StoryBlueprintKind.Logic && l.Id == bp.DefinitionNodeId)
                        {
                            clone.ParentContainer = outParent.Id;
                            clone.X = inst.X;
                            clone.Y = inst.Y;
                            outParent.Logic.Add(clone.Id);
                        }
                        outProj.LogicNodes[clone.Id] = clone;
                        result.Origin[clone.Id] = (inst.Id, l.Id);
                        break;
                    }
                    case StoryContainerNode c:
                    {
                        StoryContainerNode clone = RemapClone(c, map);
                        if (c.Id == bp.DefinitionNodeId)
                        {
                            clone.ParentContainer = outParent.Id;
                            clone.X = inst.X;
                            clone.Y = inst.Y;
                            outParent.Containers.Add(clone.Id);
                        }
                        outProj.ContainerNodes[clone.Id] = clone;
                        result.Origin[clone.Id] = (inst.Id, c.Id);
                        break;
                    }
                    case StoryPortalNode po:
                    {
                        StoryPortalNode clone = RemapClone(po, map);
                        outProj.PortalNodes[clone.Id] = clone;
                        break;
                    }
                    case StoryBlueprintInstanceNode ni:
                    {
                        StoryBlueprintInstanceNode clone = RemapClone(ni, map);
                        outProj.BlueprintInstances[clone.Id] = clone;
                        nestedInstances.Add(clone);
                        break;
                    }
                }
            }

            // Recurse into any instances that were part of the cloned subtree (nested container-blueprints).
            foreach (StoryBlueprintInstanceNode nested in nestedInstances)
                if (outProj.ContainerNodes.TryGetValue(nested.ParentContainer, out StoryContainerNode? nestedParent))
                    ExpandContainerInstance(authoring, outProj, nested, nestedParent, stack, depth + 1, result);

            stack.Remove(bp.Id);
        }

        /// <summary>The definition entity + its descendants (for a container blueprint), or just the logic node (Logic/Function).</summary>
        private static List<object> CollectDefinitionEntities(StoryProject authoring, StoryBlueprint bp)
        {
            List<object> entities = new();
            switch (bp.Kind)
            {
                case StoryBlueprintKind.Logic:
                case StoryBlueprintKind.Function:
                    if (authoring.LogicNodes.TryGetValue(bp.DefinitionNodeId, out StoryLogicNode? logic)) entities.Add(logic);
                    break;
                case StoryBlueprintKind.Container:
                    if (authoring.ContainerNodes.TryGetValue(bp.DefinitionNodeId, out StoryContainerNode? container))
                        CollectSubtree(authoring, container, entities);
                    break;
            }
            return entities;
        }

        private static void CollectSubtree(StoryProject authoring, StoryContainerNode container, List<object> entities)
        {
            entities.Add(container);
            foreach (Guid logicId in container.Logic)
                if (authoring.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) entities.Add(logic);
            foreach (Guid portalId in container.Portals)
                if (authoring.PortalNodes.TryGetValue(portalId, out StoryPortalNode? portal)) entities.Add(portal);
            foreach (Guid instId in container.Instances)
                if (authoring.BlueprintInstances.TryGetValue(instId, out StoryBlueprintInstanceNode? inst)) entities.Add(inst);
            foreach (Guid childId in container.Containers)
                if (authoring.ContainerNodes.TryGetValue(childId, out StoryContainerNode? child)) CollectSubtree(authoring, child, entities);
        }

        // ── Pass 2: function instances ────────────────────────────────────────────

        private static void InlineFunctionInstance(
            StoryProject              authoring,
            StoryLogicNode            outLogic,
            StoryFunctionInstanceNode fi,
            HashSet<Guid>             stack,
            int                       depth,
            ExpandResult              result)
        {
            outLogic.FunctionInstanceNodes.Remove(fi);

            // Seam lookups in the OUTER logic node — captured BEFORE we remove fi's seam connections.
            Guid       spineInSource   = outLogic.ContentConnections.Find(c => c.ToPoint == fi.FlowIn.Id)?.FromPoint ?? Guid.Empty;
            List<Guid> spineOutTargets = outLogic.ContentConnections.Where(c => c.FromPoint == fi.FlowOut.Id).Select(c => c.ToPoint).ToList();
            Dictionary<Guid, Guid>       inputSourceBySig   = new(); // signature input id -> outer source feeding this instance's port
            Dictionary<Guid, List<Guid>> outputTargetsBySig = new(); // signature output id -> outer targets fed by this instance's port
            foreach (StoryBlueprintPortMap ip in fi.InputPorts)
            {
                Guid src = outLogic.ContentConnections.Find(c => c.ToPoint == ip.Id)?.FromPoint ?? Guid.Empty;
                if (src != Guid.Empty) inputSourceBySig[ip.DefinitionPointId] = src;
            }
            foreach (StoryBlueprintPortMap op in fi.OutputPorts)
                outputTargetsBySig[op.DefinitionPointId] = outLogic.ContentConnections.Where(c => c.FromPoint == op.Id).Select(c => c.ToPoint).ToList();

            // Remove the instance's own seam connections from the outer graph, so error paths leave no dangling wires.
            HashSet<Guid> seamPoints = new() { fi.FlowIn.Id, fi.FlowOut.Id };
            foreach (StoryBlueprintPortMap ip in fi.InputPorts)  seamPoints.Add(ip.Id);
            foreach (StoryBlueprintPortMap op in fi.OutputPorts) seamPoints.Add(op.Id);
            outLogic.ContentConnections.RemoveAll(c => seamPoints.Contains(c.FromPoint) || seamPoints.Contains(c.ToPoint));

            // On any failure, bridge the spine straight through (source → targets) so downstream content still renders.
            void BridgeSpine()
            {
                if (spineInSource == Guid.Empty) return;
                foreach (Guid target in spineOutTargets)
                    outLogic.ContentConnections.Add(new StoryConnection { FromPoint = spineInSource, ToPoint = target });
            }

            if (depth > _DEPTH_CAP) { AddProblem(result, outLogic.ParentContainer, UiLang.T(Localization.Validation.functionNestingTooDeep), outLogic.Id); BridgeSpine(); return; }
            if (!authoring.Blueprints.TryGetValue(fi.BlueprintId, out StoryBlueprint? bp)
             || bp.Kind != StoryBlueprintKind.Function
             || !authoring.LogicNodes.TryGetValue(bp.DefinitionNodeId, out StoryLogicNode? def))
            {
                AddProblem(result, outLogic.ParentContainer, UiLang.T(Localization.Validation.functionMissing), outLogic.Id);
                BridgeSpine();
                return;
            }
            if (!stack.Add(bp.Id))
            {
                AddProblem(result, outLogic.ParentContainer, UiLang.T(Localization.Validation.functionRecursive, new Dictionary<string, object> { ["name"] = bp.Name }), outLogic.Id);
                BridgeSpine();
                return;
            }

            // Clone the definition's inner-node entities with per-instance ids; boundary SOURCE points (Entry LFlow-out
            // and each signature input) are mapped straight onto the outer sources that feed this instance.
            List<object> innerEntities = CollectInnerNodes(def);
            Dictionary<Guid, Guid> map = new();
            foreach (object e in innerEntities) CollectOwnedIds(e, id => Combine(fi.Id, id), map);
            if (spineInSource != Guid.Empty) map[def.EntryPoint.Id] = spineInSource;
            foreach (KeyValuePair<Guid, Guid> kv in inputSourceBySig) map[kv.Key] = kv.Value; // signature input id -> outer source

            List<StoryFunctionInstanceNode> nestedFns = new();
            foreach (object e in innerEntities)
            {
                object clone = RemapCloneObject(e, map);
                AddInnerCloneToLogic(outLogic, clone);
                if (clone is StoryFunctionInstanceNode nested) nestedFns.Add(nested);
            }

            // Splice connections. Boundary TARGET points (Exit LFlow-in and each signature output) are producer-side in
            // the definition but fan out to the outer consumers, so they can't be a simple id map — rewrite explicitly.
            foreach (StoryConnection c in def.ContentConnections)
            {
                if (c.ToPoint == def.ExitLFlowIn.Id)
                {
                    Guid from = MapId(map, c.FromPoint);
                    foreach (Guid target in spineOutTargets)
                        outLogic.ContentConnections.Add(new StoryConnection { FromPoint = from, ToPoint = target });
                }
                else if (outputTargetsBySig.TryGetValue(c.ToPoint, out List<Guid>? targets))
                {
                    Guid from = MapId(map, c.FromPoint);
                    foreach (Guid target in targets)
                        outLogic.ContentConnections.Add(new StoryConnection { FromPoint = from, ToPoint = target });
                }
                else
                {
                    outLogic.ContentConnections.Add(new StoryConnection { FromPoint = MapId(map, c.FromPoint), ToPoint = MapId(map, c.ToPoint) });
                }
            }

            foreach (StoryFunctionInstanceNode nested in nestedFns)
                InlineFunctionInstance(authoring, outLogic, nested, stack, depth + 1, result);

            stack.Remove(bp.Id);
        }

        /// <summary>Every inner-content node of a logic node (the items across all its inner <c>List&lt;&gt;</c> collections).</summary>
        private static List<object> CollectInnerNodes(StoryLogicNode logic)
        {
            List<object> nodes = new();
            foreach (PropertyInfo p in typeof(StoryLogicNode).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!p.PropertyType.IsGenericType || !typeof(IEnumerable).IsAssignableFrom(p.PropertyType)) continue;
                Type arg = p.PropertyType.GetGenericArguments()[0];
                if (arg == typeof(Guid) || arg == typeof(StoryConnection)) continue; // id lists + ContentConnections aren't nodes
                if (p.GetValue(logic) is IEnumerable en)
                    foreach (object? item in en) if (item is not null) nodes.Add(item);
            }
            return nodes;
        }

        private static void AddInnerCloneToLogic(StoryLogicNode logic, object clone)
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

        // ── Cloning primitives ────────────────────────────────────────────────────

        private static Guid MapId(IReadOnlyDictionary<Guid, Guid> map, Guid id) => map.TryGetValue(id, out Guid m) ? m : id;

        private static T DeepClone<T>(T entity) where T : notnull =>
            (T)JsonConvert.DeserializeObject(JsonConvert.SerializeObject(entity, _Json), entity.GetType(), _Json)!;

        private static T RemapClone<T>(T entity, IReadOnlyDictionary<Guid, Guid> map) where T : notnull =>
            (T)RemapCloneObject(entity, map);

        private static object RemapCloneObject(object entity, IReadOnlyDictionary<Guid, Guid> map)
        {
            string json = JsonConvert.SerializeObject(entity, _Json);
            foreach (KeyValuePair<Guid, Guid> kv in map)
                json = json.Replace(kv.Key.ToString(), kv.Value.ToString());
            return JsonConvert.DeserializeObject(json, entity.GetType(), _Json)!;
        }

        /// <summary>Records, for every <b>owned</b> id reachable inside <paramref name="obj"/>, a mapped id from <paramref name="idFor"/>.</summary>
        private static void CollectOwnedIds(object? obj, Func<Guid, Guid> idFor, IDictionary<Guid, Guid> map)
        {
            CollectOwnedIds(obj, idFor, map, new HashSet<object>());
        }

        private static void CollectOwnedIds(object? obj, Func<Guid, Guid> idFor, IDictionary<Guid, Guid> map, HashSet<object> seen)
        {
            if (obj is null) return;
            Type t = obj.GetType();
            if (t.IsValueType || obj is string) return;
            if (t.Namespace is null || !t.Namespace.StartsWith("DeusaldStoryCommon")) return;
            if (!seen.Add(obj)) return;

            if (t.GetProperty("Id") is { } idProp && idProp.PropertyType == typeof(Guid)
                && idProp.GetValue(obj) is Guid id && id != Guid.Empty && !map.ContainsKey(id))
                map[id] = idFor(id);

            foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                object? val = p.GetValue(obj);
                if (val is null) continue;
                if (val is IEnumerable en and not string)
                    foreach (object? item in en) CollectOwnedIds(item, idFor, map, seen);
                else
                    CollectOwnedIds(val, idFor, map, seen);
            }
        }

        /// <summary>A deterministic GUID from two GUIDs (MD5 of their 32 bytes) — stable across expansion re-runs.</summary>
        private static Guid Combine(Guid a, Guid b)
        {
            byte[] buffer = new byte[32];
            a.ToByteArray().CopyTo(buffer, 0);
            b.ToByteArray().CopyTo(buffer, 16);
            using MD5 md5 = MD5.Create();
            return new Guid(md5.ComputeHash(buffer));
        }

        private static void AddProblem(ExpandResult result, Guid containerId, string message, Guid logicNodeId = default) =>
            result.Problems.Add(new StoryProblem
            {
                Severity    = StoryProblemSeverity.Error,
                Message     = message,
                ContainerId = containerId,
                LogicNodeId = logicNodeId
            });
    }
}
