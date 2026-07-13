using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DeusaldLocalizerCommon;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Validates a whole story's <b>flow</b> graph. Two independent checks:
    /// <list type="number">
    /// <item>every flow output (Start, each logic/container exit, each portal out) is wired to something;</item>
    /// <item>storage variables register/unregister consistently — a variable can't be registered onto an occupied
    /// slot or used before it is registered, every path leaving a node carries the same set of active variables,
    /// and every variable is released before the story End.</item>
    /// </list>
    /// The balance check walks the flow from the root Start (across container boundaries and through portals,
    /// exactly like <see cref="StoryFlowNavigator"/>), processing each logic node's inner Register/Set/Unregister
    /// nodes in flow order. As a by-product it records, per logic node, which storage slots are occupied on entry —
    /// see <see cref="StoryValidationResult.FreeSlots"/> — so the editor can offer only free slots when registering.
    /// </summary>
    [PublicAPI]
    public static class StoryGraphValidator
    {
        private const int _GUARD = 4096;

        private static readonly Regex _VarRef = new Regex(@"<var=([^<>]+?)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Validates the whole story. Pass <paramref name="localization"/> to additionally check inline
        /// <c>&lt;var=Name&gt;</c> text references against the variables active where the text is shown.
        /// </summary>
        public static StoryValidationResult Validate(StoryProject project, LocProject? localization = null)
        {
            List<StoryProblem>                    problems = new();
            Dictionary<Guid, StoryRegisterVariableNode> regById = BuildRegisterIndex(project);
            Lookups                               lk       = Lookups.Build(project);

            CheckDanglingOutputs(project, problems);
            CheckChoiceOutputs(project, problems);
            CheckVariableBalance(project, lk, regById, localization, problems,
                out Dictionary<Guid, HashSet<Guid>> entryActive, out HashSet<Guid> endActive);

            return new StoryValidationResult(problems, entryActive, endActive, regById);
        }

        // ── Pass A: every flow output must be wired ────────────────────────────

        private static void CheckDanglingOutputs(StoryProject project, List<StoryProblem> problems)
        {
            foreach (StoryContainerNode container in project.ContainerNodes.Values)
            {
                bool isRoot = container.Id == project.Metadata.RootStoryContainerNodeId;

                // The container's own entry points are outputs *inside* it — flow descends from them.
                foreach (StoryConnectionPoint entry in container.EntryPoints)
                    if (!container.Connections.Exists(c => c.FromPoint == entry.Id))
                        problems.Add(new StoryProblem
                        {
                            Severity    = StoryProblemSeverity.Error,
                            Message     = $"'{PointName(entry, isRoot ? "Start" : "Entry")}' is not connected — flow entering here leads nowhere.",
                            ContainerId = container.Id,
                            PointId     = entry.Id
                        });

                // Each child logic node's exits must continue somewhere within this container.
                foreach (Guid logicId in container.Logic)
                {
                    if (!project.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) continue;

                    // In SingleSelection mode the per-exit points are internal (the Selection values); flow leaves
                    // through the single SelectionFlowOut, so that — not each exit — is the output to check.
                    if (logic.ExitMode == StoryLogicExitMode.SingleSelection)
                    {
                        if (!container.Connections.Exists(c => c.FromPoint == logic.SelectionFlowOut.Id))
                            problems.Add(new StoryProblem
                            {
                                Severity    = StoryProblemSeverity.Error,
                                Message     = $"Exit '{PointName(logic.SelectionFlowOut, "Out")}' of logic node '{NodeName(logic.Name)}' is not connected.",
                                ContainerId = container.Id,
                                LogicNodeId = logic.Id,
                                PointId     = logic.SelectionFlowOut.Id
                            });
                        continue;
                    }

                    foreach (StoryConnectionPoint exit in logic.ExitPoints)
                        if (!container.Connections.Exists(c => c.FromPoint == exit.Id))
                            problems.Add(new StoryProblem
                            {
                                Severity    = StoryProblemSeverity.Error,
                                Message     = $"Exit '{PointName(exit, "Out")}' of logic node '{NodeName(logic.Name)}' is not connected.",
                                ContainerId = container.Id,
                                LogicNodeId = logic.Id,
                                PointId     = exit.Id
                            });
                }

                // Each child container's exits must continue somewhere within this (the parent) container.
                foreach (Guid childId in container.Containers)
                {
                    if (!project.ContainerNodes.TryGetValue(childId, out StoryContainerNode? child)) continue;
                    foreach (StoryConnectionPoint exit in child.ExitPoints)
                        if (!container.Connections.Exists(c => c.FromPoint == exit.Id))
                            problems.Add(new StoryProblem
                            {
                                Severity    = StoryProblemSeverity.Error,
                                Message     = $"Exit '{PointName(exit, "Out")}' of container '{NodeName(child.Name)}' is not connected.",
                                ContainerId = container.Id,
                                PointId     = exit.Id
                            });
                }

                // Each portal's out point must continue somewhere within its container.
                foreach (Guid portalId in container.Portals)
                {
                    if (!project.PortalNodes.TryGetValue(portalId, out StoryPortalNode? portal)) continue;
                    if (!container.Connections.Exists(c => c.FromPoint == portal.OutPoint.Id))
                        problems.Add(new StoryProblem
                        {
                            Severity    = StoryProblemSeverity.Error,
                            Message     = $"Portal '{NodeName(portal.Name)}' out is not connected.",
                            ContainerId = container.Id,
                            PointId     = portal.OutPoint.Id
                        });
                }
            }
        }

        // ── Choice outputs must each reach an exit ─────────────────────────────

        /// <summary>Flags any Choice option whose flow-out isn't wired — every choice must lead to an exit.</summary>
        private static void CheckChoiceOutputs(StoryProject project, List<StoryProblem> problems)
        {
            foreach (StoryLogicNode logic in project.LogicNodes.Values)
                foreach (StoryChoiceNode choice in logic.ChoiceNodes)
                    foreach (StoryChoiceOption option in choice.Options)
                        if (!logic.ContentConnections.Exists(c => c.FromPoint == option.FlowOut.Id))
                            problems.Add(Inner(logic, choice.Id,
                                $"Choice '{NodeName(option.Name)}' in '{NodeName(logic.Name)}' is not connected to an exit."));
        }

        // ── Pass B: register/unregister balance from Start ─────────────────────

        private static void CheckVariableBalance(
            StoryProject project, Lookups lk, Dictionary<Guid, StoryRegisterVariableNode> regById,
            LocProject? localization, List<StoryProblem> problems,
            out Dictionary<Guid, HashSet<Guid>> entryActive, out HashSet<Guid> endActive)
        {
            entryActive = new Dictionary<Guid, HashSet<Guid>>();
            endActive   = new HashSet<Guid>();
            HashSet<Guid> divergenceFlagged = new();
            Dictionary<Guid, StoryLogicNode> regOwnerById = BuildRegisterOwnerIndex(project);

            if (!project.ContainerNodes.TryGetValue(project.Metadata.RootStoryContainerNodeId, out StoryContainerNode? root)
                || root.EntryPoints.Count == 0)
                return;

            HashSet<Guid> endReached = endActive; // captured by the local recursion

            Dictionary<Guid, HashSet<Guid>> ea = entryActive; // captured by the local recursion

            void VisitLogic(StoryLogicNode logic, HashSet<Guid> incoming, int guard)
            {
                if (guard > _GUARD) return;

                if (ea.TryGetValue(logic.Id, out HashSet<Guid>? prev))
                {
                    if (!prev.SetEquals(incoming) && divergenceFlagged.Add(logic.Id))
                        problems.Add(new StoryProblem
                        {
                            Severity    = StoryProblemSeverity.Error,
                            Message     = $"Variable state differs across the paths reaching '{NodeName(logic.Name)}' — a variable is left registered on some paths but not others.",
                            ContainerId = logic.ParentContainer,
                            LogicNodeId = logic.Id
                        });
                    return;
                }

                ea[logic.Id] = new HashSet<Guid>(incoming);
                CheckTextReferences(logic, incoming, regById, localization, problems);
                HashSet<Guid> active = new(incoming);
                ApplyOps(project, logic, active, regById, problems);

                // SingleSelection collapses every internal exit into one continuation (SelectionFlowOut);
                // otherwise each exit continues independently. Both carry the same active-variable set.
                IEnumerable<Guid> continuations = logic.ExitMode == StoryLogicExitMode.SingleSelection
                    ? new[] { logic.SelectionFlowOut.Id }
                    : logic.ExitPoints.Select(e => e.Id);

                foreach (Guid outPointId in continuations)
                {
                    Step step = Follow(project, lk, logic.ParentContainer, outPointId, 0);
                    switch (step.Kind)
                    {
                        case StepKind.Logic: VisitLogic(step.Logic!, active, guard + 1); break;
                        case StepKind.End:   CheckReleasedAtEnd(project, active, endReached, regById, regOwnerById, problems); break;
                        // Dangling exits are reported by Pass A; stop this branch.
                    }
                }
            }

            Step start = Follow(project, lk, root, root.EntryPoints[0].Id, 0);
            switch (start.Kind)
            {
                case StepKind.Logic: VisitLogic(start.Logic!, new HashSet<Guid>(), 0); break;
                case StepKind.End:   break; // empty story — nothing to release
            }
        }

        /// <summary>Applies a logic node's inner Register/Set/Unregister operations, in flow order, to <paramref name="active"/>.</summary>
        private static void ApplyOps(
            StoryProject project, StoryLogicNode logic, HashSet<Guid> active, Dictionary<Guid, StoryRegisterVariableNode> regById,
            List<StoryProblem> problems)
        {
            foreach (StorageOp op in StoryLogicFlow.StorageOps(project, logic))
            {
                switch (op.Kind)
                {
                    case StorageOpKind.Register:
                        StoryRegisterVariableNode reg = op.Register!;
                        if (!active.Add(reg.Id))
                        {
                            problems.Add(Inner(logic, reg.Id, $"Variable '{NodeName(reg.Name)}' is registered again while already active."));
                            break;
                        }
                        if (reg.SlotIndex < 0 || reg.SlotIndex >= StorageSlots.Count(reg.Type))
                            problems.Add(Inner(logic, reg.Id, $"Slot {StorageSlots.Label(reg.Type, reg.SlotIndex)} is out of range for {reg.Type} storage."));
                        else if (active.Any(id => id != reg.Id && regById.TryGetValue(id, out StoryRegisterVariableNode? other)
                                                  && other.Type == reg.Type && other.SlotIndex == reg.SlotIndex))
                            problems.Add(Inner(logic, reg.Id, $"Slot {StorageSlots.Label(reg.Type, reg.SlotIndex)} is already in use by another registered variable."));
                        break;

                    case StorageOpKind.Set:
                        if (!active.Contains(op.TargetRegisterId))
                            problems.Add(Inner(logic, op.InnerId, $"Set of variable '{TargetName(op.TargetRegisterId, regById)}' that isn't registered on this path."));
                        break;

                    case StorageOpKind.Unregister:
                        if (!active.Remove(op.TargetRegisterId))
                            problems.Add(Inner(logic, op.InnerId, $"Unregister of variable '{TargetName(op.TargetRegisterId, regById)}' that isn't registered on this path."));
                        break;
                }
            }
        }

        /// <summary>
        /// Flags any inline <c>&lt;var=Name&gt;</c> reference in this node's localized text that names a storage
        /// variable not available anywhere in this node — i.e. neither active on entry nor registered within the
        /// node. (Intra-node ordering of register/unregister vs. the text is not modelled, so a variable available at
        /// any point in the node is accepted.)
        /// </summary>
        private static void CheckTextReferences(
            StoryLogicNode logic, HashSet<Guid> entryActive, Dictionary<Guid, StoryRegisterVariableNode> regById,
            LocProject? localization, List<StoryProblem> problems)
        {
            if (localization is null || logic.LocalizationNodes.Count == 0) return;

            HashSet<string> activeNames = new(StringComparer.OrdinalIgnoreCase);
            foreach (Guid id in entryActive)
                if (regById.TryGetValue(id, out StoryRegisterVariableNode? reg) && !string.IsNullOrWhiteSpace(reg.Name))
                    activeNames.Add(reg.Name);
            foreach (StoryRegisterVariableNode reg in logic.RegisterVariableNodes)
                if (!string.IsNullOrWhiteSpace(reg.Name))
                    activeNames.Add(reg.Name);

            HashSet<string> reported = new(StringComparer.OrdinalIgnoreCase);
            foreach (StoryLocalizationNode loc in logic.LocalizationNodes)
            {
                string text = StoryLogicRenderer.LocalizedText(localization, loc.SelectedKeyId);
                if (text.Length == 0) continue;

                foreach (Match m in _VarRef.Matches(text))
                {
                    string name = m.Groups[1].Value.Trim();
                    if (activeNames.Contains(name) || !reported.Add(name)) continue;
                    problems.Add(new StoryProblem
                    {
                        Severity    = StoryProblemSeverity.Error,
                        Message     = $"Text references variable '{name}' that isn't registered where '{NodeName(logic.Name)}' is shown.",
                        ContainerId = logic.ParentContainer,
                        LogicNodeId = logic.Id,
                        InnerNodeId = loc.Id
                    });
                }
            }
        }

        private static void CheckReleasedAtEnd(
            StoryProject project, HashSet<Guid> active, HashSet<Guid> endActive,
            Dictionary<Guid, StoryRegisterVariableNode> regById,
            Dictionary<Guid, StoryLogicNode> regOwnerById, List<StoryProblem> problems)
        {
            HashSet<Guid> releasedAtEnd = new(project.Metadata.UnregisterAtEnd);
            foreach (Guid id in active)
            {
                endActive.Add(id); // a variable still registered when the story reaches End — a release candidate
                if (releasedAtEnd.Contains(id)) continue; // released by the story End node
                // Navigate to the node holding the still-active registration (the register id in `active`
                // uniquely identifies it — a variable can't be registered again while active), not the last
                // logic node before The End.
                StoryProblem problem = new()
                {
                    Severity    = StoryProblemSeverity.Error,
                    Message     = $"Variable '{TargetName(id, regById)}' is still registered when the story reaches The End — unregister it first (or release it on the End node).",
                    InnerNodeId = id
                };
                if (regOwnerById.TryGetValue(id, out StoryLogicNode? owner))
                {
                    problem.LogicNodeId = owner.Id;
                    problem.ContainerId = owner.ParentContainer;
                }
                problems.Add(problem);
            }
        }

        // ── Flow following (mirrors StoryFlowNavigator, but returns the logic node) ──

        private enum StepKind { Logic, End, Dangling }

        private readonly struct Step
        {
            public Step(StepKind kind, StoryLogicNode? logic)
            {
                Kind  = kind;
                Logic = logic;
            }

            public StepKind        Kind  { get; }
            public StoryLogicNode? Logic { get; }
        }

        private static Step Follow(StoryProject project, Lookups lk, Guid containerId, Guid outputPointId, int guard)
        {
            if (!project.ContainerNodes.TryGetValue(containerId, out StoryContainerNode? container))
                return new Step(StepKind.Dangling, null);
            return Follow(project, lk, container, outputPointId, guard);
        }

        private static Step Follow(StoryProject project, Lookups lk, StoryContainerNode container, Guid outputPointId, int guard)
        {
            if (guard > _GUARD) return new Step(StepKind.Dangling, null);

            StoryConnection? conn = container.Connections.Find(c => c.FromPoint == outputPointId);
            if (conn is null) return new Step(StepKind.Dangling, null);

            return ArriveAt(project, lk, conn.ToPoint, guard + 1);
        }

        private static Step ArriveAt(StoryProject project, Lookups lk, Guid pointId, int guard)
        {
            if (guard > _GUARD) return new Step(StepKind.Dangling, null);

            if (lk.LogicByEntry.TryGetValue(pointId, out StoryLogicNode? logic))
                return new Step(StepKind.Logic, logic);

            if (lk.ContainerByEntry.TryGetValue(pointId, out StoryContainerNode? child))
                return Follow(project, lk, child, pointId, guard + 1);

            if (lk.ContainerByExit.TryGetValue(pointId, out StoryContainerNode? owner))
            {
                if (owner.Id == project.Metadata.RootStoryContainerNodeId)
                    return new Step(StepKind.End, null);
                if (!project.ContainerNodes.TryGetValue(owner.ParentContainer, out StoryContainerNode? parent))
                    return new Step(StepKind.Dangling, null);
                return Follow(project, lk, parent, pointId, guard + 1);
            }

            if (lk.PortalByIn.TryGetValue(pointId, out StoryPortalNode? portal)
                && project.ContainerNodes.TryGetValue(portal.ParentContainer, out StoryContainerNode? portalContainer))
                return Follow(project, lk, portalContainer, portal.OutPoint.Id, guard + 1);

            return new Step(StepKind.Dangling, null);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static Dictionary<Guid, StoryRegisterVariableNode> BuildRegisterIndex(StoryProject project)
        {
            Dictionary<Guid, StoryRegisterVariableNode> map = new();
            foreach (StoryLogicNode logic in project.LogicNodes.Values)
                foreach (StoryRegisterVariableNode reg in logic.RegisterVariableNodes)
                    map[reg.Id] = reg;
            return map;
        }

        /// <summary>Maps each register-node id to the logic node that owns it — so a variable still active at
        /// The End can be navigated to its registration node rather than the last node before End.</summary>
        private static Dictionary<Guid, StoryLogicNode> BuildRegisterOwnerIndex(StoryProject project)
        {
            Dictionary<Guid, StoryLogicNode> map = new();
            foreach (StoryLogicNode logic in project.LogicNodes.Values)
                foreach (StoryRegisterVariableNode reg in logic.RegisterVariableNodes)
                    map[reg.Id] = logic;
            return map;
        }

        private static StoryProblem Inner(StoryLogicNode logic, Guid innerId, string message) => new()
        {
            Severity    = StoryProblemSeverity.Error,
            Message     = message,
            ContainerId = logic.ParentContainer,
            LogicNodeId = logic.Id,
            InnerNodeId = innerId
        };

        private static string TargetName(Guid id, Dictionary<Guid, StoryRegisterVariableNode> regById) =>
            regById.TryGetValue(id, out StoryRegisterVariableNode? reg) ? NodeName(reg.Name) : "(unknown)";

        private static string NodeName(string name) => string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name;

        private static string PointName(StoryConnectionPoint point, string fallback) =>
            string.IsNullOrWhiteSpace(point.Name) ? fallback : point.Name;

        private sealed class Lookups
        {
            public Dictionary<Guid, StoryLogicNode>     LogicByEntry     { get; } = new();
            public Dictionary<Guid, StoryContainerNode> ContainerByEntry { get; } = new();
            public Dictionary<Guid, StoryContainerNode> ContainerByExit  { get; } = new();
            public Dictionary<Guid, StoryPortalNode>    PortalByIn       { get; } = new();

            public static Lookups Build(StoryProject project)
            {
                Lookups lk = new();

                foreach (StoryLogicNode logic in project.LogicNodes.Values)
                    lk.LogicByEntry[logic.EntryPoint.Id] = logic;

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

    /// <summary>The outcome of <see cref="StoryGraphValidator.Validate"/>: the problems found, plus the per-logic-node
    /// active-variable state used to compute which storage slots are free when registering at that node.</summary>
    public sealed class StoryValidationResult
    {
        private readonly Dictionary<Guid, HashSet<Guid>>              _EntryActive;
        private readonly Dictionary<Guid, StoryRegisterVariableNode>  _RegById;

        public StoryValidationResult(
            List<StoryProblem> problems, Dictionary<Guid, HashSet<Guid>> entryActive,
            HashSet<Guid> endActive, Dictionary<Guid, StoryRegisterVariableNode> regById)
        {
            Problems      = problems;
            _EntryActive  = entryActive;
            EndActive     = endActive;
            _RegById      = regById;
        }

        public List<StoryProblem> Problems { get; }

        /// <summary>Register-node ids of variables still registered when the story reaches The End on some path — the
        /// candidates the End node can release. A variable unregistered on every path never appears here.</summary>
        public IReadOnlyCollection<Guid> EndActive { get; }

        /// <summary>Whether the logic node is reachable from Start (so its slot usage is known).</summary>
        public bool IsReachable(Guid logicId) => _EntryActive.ContainsKey(logicId);

        /// <summary>
        /// The slot indices still free for <paramref name="type"/> on entry to <paramref name="logicId"/> — every slot
        /// of that type minus those occupied by variables still registered on the path here. Null when the node is
        /// unreachable (its state is unknown), so callers can fall back to offering every slot with a warning.
        /// </summary>
        public IReadOnlyList<int>? FreeSlots(Guid logicId, StorageVariableType type)
        {
            if (!_EntryActive.TryGetValue(logicId, out HashSet<Guid>? active)) return null;

            HashSet<int> used = new();
            foreach (Guid id in active)
                if (_RegById.TryGetValue(id, out StoryRegisterVariableNode? reg) && reg.Type == type)
                    used.Add(reg.SlotIndex);

            List<int> free = new();
            for (int i = 0; i < StorageSlots.Count(type); ++i)
                if (!used.Contains(i)) free.Add(i);
            return free;
        }
    }
}
