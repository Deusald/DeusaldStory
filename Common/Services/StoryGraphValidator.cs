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
            CheckChoices(project, problems);
            CheckConditionFlows(project, problems);
            CheckIncomingVariableContract(project, problems);
            CheckGamebookText(project, localization, problems);
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
                            Message     = UiLang.T(Localization.Validation.entryNotConnected, new Dictionary<string, object> { ["point"] = PointName(entry, isRoot ? UiLang.T(Localization.Validation.pointStart) : UiLang.T(Localization.Validation.pointEntry)) }),
                            ContainerId = container.Id,
                            PointId     = entry.Id
                        });

                // Each child logic node's exits must continue somewhere within this container.
                foreach (Guid logicId in container.Logic)
                {
                    if (!project.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) continue;

                    // SinglePath collapses every choice into one VFlow output; ManyPaths draws one Flow output per
                    // choice. Each drawn output must continue somewhere within this container.
                    if (logic.ExitMode == StoryLogicExitMode.SinglePath)
                    {
                        if (!container.Connections.Exists(c => c.FromPoint == logic.VFlowOut.Id))
                            problems.Add(new StoryProblem
                            {
                                Severity    = StoryProblemSeverity.Error,
                                Message     = UiLang.T(Localization.Validation.variablesOutputNotConnected, new Dictionary<string, object> { ["node"] = NodeName(logic.Name) }),
                                ContainerId = container.Id,
                                LogicNodeId = logic.Id,
                                PointId     = logic.VFlowOut.Id
                            });
                        continue;
                    }

                    foreach (StoryChoice choice in logic.Choices)
                        if (!container.Connections.Exists(c => c.FromPoint == choice.OuterFlowOut.Id))
                            problems.Add(new StoryProblem
                            {
                                Severity    = StoryProblemSeverity.Error,
                                Message     = UiLang.T(Localization.Validation.choiceNotConnected, new Dictionary<string, object> { ["choice"] = NodeName(choice.Name), ["logic"] = NodeName(logic.Name) }),
                                ContainerId = container.Id,
                                LogicNodeId = logic.Id,
                                PointId     = choice.OuterFlowOut.Id
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
                                Message     = UiLang.T(Localization.Validation.containerExitNotConnected, new Dictionary<string, object> { ["exit"] = PointName(exit, UiLang.T(Localization.Validation.pointOut)), ["container"] = NodeName(child.Name) }),
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
                            Message     = UiLang.T(Localization.Validation.portalOutNotConnected, new Dictionary<string, object> { ["portal"] = NodeName(portal.Name) }),
                            ContainerId = container.Id,
                            PointId     = portal.OutPoint.Id
                        });
                }
            }
        }

        // ── Choices & auto-resolution ──────────────────────────────────────────

        /// <summary>
        /// Validates each logic node's Exit-node choices: at least one choice; when App auto-resolution is enabled
        /// (the Variables input is wired), exactly one locked Else and every non-Else choice carries a condition;
        /// and in SinglePath mode every choice assigns a value for each declared variable.
        /// </summary>
        private static void CheckChoices(StoryProject project, List<StoryProblem> problems)
        {
            foreach (StoryLogicNode logic in project.LogicNodes.Values)
            {
                if (logic.Choices.Count == 0)
                {
                    problems.Add(new StoryProblem
                    {
                        Severity    = StoryProblemSeverity.Error,
                        Message     = UiLang.T(Localization.Validation.logicNoChoices, new Dictionary<string, object> { ["node"] = NodeName(logic.Name) }),
                        ContainerId = logic.ParentContainer,
                        LogicNodeId = logic.Id
                    });
                    continue;
                }

                bool autoMode = FromInto(logic, logic.ExitVariablesIn.Id) != Guid.Empty;
                if (autoMode)
                {
                    int elseCount = logic.Choices.Count(c => c.IsElse);
                    if (elseCount != 1)
                        problems.Add(Node(logic, elseCount == 0
                            ? UiLang.T(Localization.Validation.autoNeedsOneElse, new Dictionary<string, object> { ["node"] = NodeName(logic.Name) })
                            : UiLang.T(Localization.Validation.autoMultipleElse, new Dictionary<string, object> { ["node"] = NodeName(logic.Name) })));

                    foreach (StoryChoice c in logic.Choices)
                        if (!c.IsElse && c.Condition is null)
                            problems.Add(Node(logic, UiLang.T(Localization.Validation.choiceNoCondition, new Dictionary<string, object> { ["choice"] = NodeName(c.Name), ["logic"] = NodeName(logic.Name) })));
                }

                if (logic.ExitMode == StoryLogicExitMode.SinglePath)
                    foreach (StoryChoice c in logic.Choices)
                        foreach (StoryDeclaredVariable dv in logic.DeclaredVariables)
                            if (c.VariableValues.Find(v => v.DeclaredVarId == dv.Id) is null)
                                problems.Add(Node(logic, UiLang.T(Localization.Validation.choiceNoVariableValue, new Dictionary<string, object> { ["choice"] = NodeName(c.Name), ["logic"] = NodeName(logic.Name), ["var"] = NodeName(dv.Name) })));
            }
        }

        /// <summary>
        /// Validates each logic node's Condition nodes: every variable wired into a Condition's Variables input must be
        /// a <b>constant</b> source, so the branch resolves the same in the App and the printed Gamebook (a live
        /// variable would be unknown in print / would dimension sections). The condition's structure is edited via the
        /// modal and needs no further check here.
        /// </summary>
        private static void CheckConditionFlows(StoryProject project, List<StoryProblem> problems)
        {
            foreach (StoryLogicNode logic in project.LogicNodes.Values)
                foreach (StoryConditionFlowNode cf in logic.ConditionFlowNodes)
                    foreach (StoryConnection c in logic.ContentConnections.Where(c => c.ToPoint == cf.VariablesIn.Id))
                        if (!IsConstantSource(project, logic, c.FromPoint))
                            problems.Add(Inner(logic, cf.Id,
                                UiLang.T(Localization.Validation.conditionNonConstant, new Dictionary<string, object> { ["cond"] = NodeName(cf.Name), ["logic"] = NodeName(logic.Name) })));
        }

        /// <summary>Whether the output wired at <paramref name="fromPoint"/> (resolved through any portal) is a constant value source.</summary>
        private static bool IsConstantSource(StoryProject project, StoryLogicNode logic, Guid fromPoint)
        {
            Guid src = logic.ResolvePortalSource(fromPoint);
            if (logic.ConstantVariableNodes.Exists(n => n.OutPoint.Id == src)) return true;
            if (logic.GetVariableNodes.Exists(n => n.SlotOutPoint.Id == src)) return true; // Gamebook slot tag — constant
            if (logic.ExternalVariableNodes.Find(n => n.OutPoint.Id == src) is StoryExternalVariableNode ev)
            {
                StoryVariable? v = StoryBuiltInVariables.Find(ev.SelectedVariableId)
                    ?? (project.Variables.TryGetValue(ev.SelectedVariableId, out StoryVariable? found) ? found : null);
                return v is not null && (v.IsConstant || StoryBuiltInVariables.IsBuiltIn(v.Id));
            }
            // Prev Exit / incoming declared variables are exposed as constants inside the node.
            return StorySelectionResolver.IncomingVariables(project, logic).Exists(d => d.Id == src);
        }

        // ── Incoming-variable contract ─────────────────────────────────────────

        /// <summary>
        /// For every node that declares an AcceptVariables contract (<see cref="StoryLogicNode.ExpectedVariables"/>),
        /// checks that its wired-in upstream Single-path node provides <b>exactly</b> that set — same variable names and,
        /// for each name, the same set of possible values. This is what makes a reusable Logic blueprint safe: an
        /// instance whose upstream doesn't match the contract is flagged instead of silently rendering blank values.
        /// </summary>
        private static void CheckIncomingVariableContract(StoryProject project, List<StoryProblem> problems)
        {
            foreach (StoryLogicNode logic in project.LogicNodes.Values)
            {
                if (!logic.AcceptVariables || logic.ExpectedVariables.Count == 0) continue;

                string nodeName = string.IsNullOrWhiteSpace(logic.Name) ? UiLang.T(Localization.Common.Placeholders.unnamed) : logic.Name;
                StoryLogicNode? upstream = StorySelectionResolver.SourceNode(project, logic);
                if (upstream is null)
                {
                    problems.Add(new StoryProblem
                    {
                        Severity    = StoryProblemSeverity.Error,
                        Message     = UiLang.T(Localization.Validation.expectsIncomingNotWired, new Dictionary<string, object> { ["node"] = nodeName }),
                        ContainerId = logic.ParentContainer,
                        LogicNodeId = logic.Id
                    });
                    continue;
                }

                HashSet<string> expected = logic.ExpectedVariables.Select(v => v.Name).ToHashSet();
                HashSet<string> provided = upstream.DeclaredVariables.Select(v => v.Name).ToHashSet();
                List<string>    missing  = expected.Except(provided).OrderBy(n => n).ToList();
                List<string>    extra    = provided.Except(expected).OrderBy(n => n).ToList();

                if (missing.Count > 0 || extra.Count > 0)
                {
                    string detail = string.Join("; ",
                        new[]
                        {
                            missing.Count > 0 ? UiLang.T(Localization.Validation.contractMissing, new Dictionary<string, object> { ["names"] = string.Join(", ", missing) }) : null,
                            extra.Count   > 0 ? UiLang.T(Localization.Validation.contractUnexpected, new Dictionary<string, object> { ["names"] = string.Join(", ", extra) }) : null
                        }.Where(s => s is not null));
                    problems.Add(new StoryProblem
                    {
                        Severity    = StoryProblemSeverity.Error,
                        Message     = UiLang.T(Localization.Validation.contractMismatch, new Dictionary<string, object> { ["node"] = nodeName, ["detail"] = detail }),
                        ContainerId = logic.ParentContainer,
                        LogicNodeId = logic.Id
                    });
                    continue;
                }

                // Same names — every variable's set of possible values must match too.
                foreach (StoryDeclaredVariable exp in logic.ExpectedVariables)
                {
                    StoryDeclaredVariable? prov = upstream.DeclaredVariables.Find(v => v.Name == exp.Name);
                    if (prov is null) continue;
                    if (!exp.PossibleValues.ToHashSet().SetEquals(prov.PossibleValues))
                        problems.Add(new StoryProblem
                        {
                            Severity    = StoryProblemSeverity.Error,
                            Message     = UiLang.T(Localization.Validation.contractValuesDiffer, new Dictionary<string, object> { ["node"] = nodeName, ["var"] = exp.Name }),
                            ContainerId = logic.ParentContainer,
                            LogicNodeId = logic.Id
                        });
                }
            }
        }

        /// <summary>Reports SmartFormat render failures in each node's Gamebook text (e.g. a plain Variable unavailable in the Gamebook).</summary>
        private static void CheckGamebookText(StoryProject project, LocProject? localization, List<StoryProblem> problems)
        {
            foreach (StoryLogicNode logic in project.LogicNodes.Values)
            {
                // Validate the node exactly as the Gamebook prints it: one render per generated section, each pinning the
                // incoming declared-variable constants. Rendering once with empty values would resolve a Prev Exit Variable
                // to "" and raise false SmartFormat errors (e.g. a {var:choose(…)} that never sees an empty value in print).
                StoryGamebookPreview.Result gamebook = StoryGamebookPreview.Build(project, localization, logic);
                HashSet<string>             seen     = new();
                foreach (StoryGamebookPreview.Section section in gamebook.Sections)
                    foreach (string error in section.Rendered.Errors)
                        if (seen.Add(error))
                            problems.Add(Node(logic, UiLang.T(Localization.Validation.gamebookTextPrefix, new Dictionary<string, object> { ["node"] = NodeName(logic.Name), ["error"] = error })));
            }
        }

        private static Guid FromInto(StoryLogicNode logic, Guid toPoint) =>
            logic.ContentConnections.Find(c => c.ToPoint == toPoint)?.FromPoint ?? Guid.Empty;

        private static StoryProblem Node(StoryLogicNode logic, string message) => new()
        {
            Severity    = StoryProblemSeverity.Error,
            Message     = message,
            ContainerId = logic.ParentContainer,
            LogicNodeId = logic.Id
        };

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
                            Message     = UiLang.T(Localization.Validation.variableStateDiverges, new Dictionary<string, object> { ["node"] = NodeName(logic.Name) }),
                            ContainerId = logic.ParentContainer,
                            LogicNodeId = logic.Id
                        });
                    return;
                }

                ea[logic.Id] = new HashSet<Guid>(incoming);
                CheckTextReferences(logic, incoming, regById, localization, problems);
                CheckGetRefs(project, logic, incoming, problems);
                CheckRandomizedInstruction(project, logic, problems);
                HashSet<Guid> active = new(incoming);
                ApplyOps(project, logic, active, regById, problems);

                // SinglePath collapses every choice into one continuation (VFlowOut); ManyPaths continues each choice
                // independently. Both carry the same active-variable set.
                IEnumerable<Guid> continuations = logic.ExitMode == StoryLogicExitMode.SinglePath
                    ? new[] { logic.VFlowOut.Id }
                    : logic.Choices.Select(c => c.OuterFlowOut.Id);

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
                            problems.Add(Inner(logic, reg.Id, UiLang.T(Localization.Validation.variableReRegistered, new Dictionary<string, object> { ["node"] = NodeName(reg.Name) })));
                            break;
                        }
                        if (reg.SlotIndex < 0 || reg.SlotIndex >= StorageSlots.Count(reg.Type))
                            problems.Add(Inner(logic, reg.Id, UiLang.T(Localization.Validation.slotOutOfRange, new Dictionary<string, object> { ["slot"] = StorageSlots.Label(reg.Type, reg.SlotIndex), ["type"] = reg.Type })));
                        else if (active.Any(id => id != reg.Id && regById.TryGetValue(id, out StoryRegisterVariableNode? other)
                                                  && other.Type == reg.Type && other.SlotIndex == reg.SlotIndex))
                            problems.Add(Inner(logic, reg.Id, UiLang.T(Localization.Validation.slotInUse, new Dictionary<string, object> { ["slot"] = StorageSlots.Label(reg.Type, reg.SlotIndex) })));
                        break;

                    case StorageOpKind.Set:
                        CheckSetTarget(project, logic, op, active, regById, problems);
                        break;

                    case StorageOpKind.Unregister:
                        if (!active.Remove(op.TargetRegisterId))
                            problems.Add(Inner(logic, op.InnerId, UiLang.T(Localization.Validation.unregisterUnregistered, new Dictionary<string, object> { ["target"] = TargetName(op.TargetRegisterId, regById) })));
                        break;
                }
            }
        }

        /// <summary>
        /// Checks a Set node's target is registered on this path. A <see cref="StorageVariableRefMode.Specific"/> set
        /// names its register directly; a <see cref="StorageVariableRefMode.ByType"/> set takes the name from its
        /// wired Name port, so the wire must carry a constant and <i>every</i> name that constant can take must name a
        /// registered variable of the chosen type that is active here.
        /// </summary>
        private static void CheckSetTarget(
            StoryProject project, StoryLogicNode logic, StorageOp op, HashSet<Guid> active,
            Dictionary<Guid, StoryRegisterVariableNode> regById, List<StoryProblem> problems)
        {
            StorySetVariableNode set = op.Set!;

            if (set.RefMode == StorageVariableRefMode.Specific)
            {
                if (!active.Contains(op.TargetRegisterId))
                    problems.Add(Inner(logic, op.InnerId, UiLang.T(Localization.Validation.setUnregistered, new Dictionary<string, object> { ["target"] = TargetName(op.TargetRegisterId, regById) })));
                return;
            }

            List<string>? names = StoryLogicFlow.PossibleVariableValues(project, logic, StoryLogicFlow.FromInto(logic, set.NameIn.Id));
            if (names is null)
            {
                problems.Add(Inner(logic, set.Id, UiLang.T(Localization.Validation.refNameNotConstant, new Dictionary<string, object> { ["type"] = set.RefType })));
                return;
            }

            foreach (string name in names)
            {
                StoryRegisterVariableNode? reg = StoryLogicFlow.FindRegisterByName(project, name, set.RefType);
                if (reg is null)
                    problems.Add(Inner(logic, set.Id, UiLang.T(Localization.Validation.refNameNotFound, new Dictionary<string, object> { ["name"] = NodeName(name), ["type"] = set.RefType })));
                else if (!active.Contains(reg.Id))
                    problems.Add(Inner(logic, set.Id, UiLang.T(Localization.Validation.setUnregistered, new Dictionary<string, object> { ["target"] = NodeName(name) })));
            }
        }

        /// <summary>
        /// Checks every <see cref="StorageVariableRefMode.ByType"/> Get node in this logic node the same way: its Name
        /// port must carry a constant, and each name that constant can take must be a variable of the chosen type
        /// available in this node. A Get sits off the flow spine, so — as with the text references below — a variable
        /// available at any point in the node is accepted.
        /// </summary>
        private static void CheckGetRefs(
            StoryProject project, StoryLogicNode logic, HashSet<Guid> entryActive, List<StoryProblem> problems)
        {
            foreach (StoryGetVariableNode gv in logic.GetVariableNodes)
            {
                if (gv.RefMode != StorageVariableRefMode.ByType) continue;

                List<string>? names = StoryLogicFlow.PossibleVariableValues(project, logic, StoryLogicFlow.FromInto(logic, gv.NameIn.Id));
                if (names is null)
                {
                    problems.Add(Inner(logic, gv.Id, UiLang.T(Localization.Validation.refNameNotConstant, new Dictionary<string, object> { ["type"] = gv.RefType })));
                    continue;
                }

                foreach (string name in names)
                {
                    StoryRegisterVariableNode? reg = StoryLogicFlow.FindRegisterByName(project, name, gv.RefType);
                    if (reg is null)
                        problems.Add(Inner(logic, gv.Id, UiLang.T(Localization.Validation.refNameNotFound, new Dictionary<string, object> { ["name"] = NodeName(name), ["type"] = gv.RefType })));
                    else if (!entryActive.Contains(reg.Id) && !logic.RegisterVariableNodes.Exists(n => n.Id == reg.Id))
                        problems.Add(Inner(logic, gv.Id, UiLang.T(Localization.Validation.refNameUnavailable, new Dictionary<string, object> { ["name"] = NodeName(name), ["node"] = NodeName(logic.Name) })));
                }
            }
        }

        /// <summary>
        /// Checks every Randomized Instruction node: it must name a result token and have a non-empty range. When its
        /// Branch input is wired the source must be enumerable (constant or an incoming declared variable) and every
        /// value it can take must have a configured, non-empty range.
        /// </summary>
        private static void CheckRandomizedInstruction(StoryProject project, StoryLogicNode logic, List<StoryProblem> problems)
        {
            foreach (StoryRandomizedInstructionNode ri in logic.RandomizedInstructionNodes)
            {
                string nodeName = NodeName(string.IsNullOrWhiteSpace(ri.ResultToken)
                    ? UiLang.T(Localization.Editor.Nodes.Labels.randomizedInstruction) : ri.ResultToken);

                if (string.IsNullOrWhiteSpace(ri.ResultToken))
                    problems.Add(Inner(logic, ri.Id, UiLang.T(Localization.Validation.randomResultTokenEmpty, new Dictionary<string, object> { ["node"] = nodeName })));

                Guid          branchFrom   = StoryLogicFlow.FromInto(logic, ri.BranchIn.Id);
                List<string>? branchValues = branchFrom == Guid.Empty ? null : StoryLogicFlow.BranchValues(project, logic, branchFrom);

                if (branchFrom == Guid.Empty)
                {
                    if (ri.DefaultRange.Count == 0)
                        problems.Add(Inner(logic, ri.Id, UiLang.T(Localization.Validation.randomRangeEmpty, new Dictionary<string, object> { ["node"] = nodeName })));
                }
                else if (branchValues is null)
                    problems.Add(Inner(logic, ri.Id, UiLang.T(Localization.Validation.randomBranchNotConstant, new Dictionary<string, object> { ["node"] = nodeName })));
                else
                    foreach (string value in branchValues)
                    {
                        StoryRandomRange? range = ri.BranchRanges.Find(r => string.Equals(r.BranchValue, value, StringComparison.OrdinalIgnoreCase));
                        if (range is null || range.Values.Count == 0)
                            problems.Add(Inner(logic, ri.Id, UiLang.T(Localization.Validation.randomBranchMissingRange, new Dictionary<string, object> { ["node"] = nodeName, ["value"] = NodeName(value) })));
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
                        Message     = UiLang.T(Localization.Validation.textRefUnregistered, new Dictionary<string, object> { ["name"] = name, ["node"] = NodeName(logic.Name) }),
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
                    Message     = UiLang.T(Localization.Validation.variableNotReleasedAtEnd, new Dictionary<string, object> { ["target"] = TargetName(id, regById) }),
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
            regById.TryGetValue(id, out StoryRegisterVariableNode? reg) ? NodeName(reg.Name) : UiLang.T(Localization.Validation.unknownTarget);

        private static string NodeName(string name) => string.IsNullOrWhiteSpace(name) ? UiLang.T(Localization.Common.Placeholders.unnamed) : name;

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
