using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DeusaldLocalizerCommon;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Validates a whole story's <b>flow</b> graph and its variable usage:
    /// <list type="number">
    /// <item>every flow output (Start, each logic/container exit, each portal out) is wired to something;</item>
    /// <item>each logic node's Exit-node choices / auto-resolution are well-formed;</item>
    /// <item>Condition and Randomized-Instruction inputs are constant/enumerable where required;</item>
    /// <item>inline <c>&lt;var=Name&gt;</c> text references name a real variable;</item>
    /// <item>container slot <b>reservations</b> don't collide — an Internal variable's physical slot can be reserved by
    /// at most one variable on any nesting path (a Scenario-lifespan variable reserves its slot for the whole story;
    /// a Chapter-lifespan variable only inside the containers that use it).</item>
    /// </list>
    /// Since variables are now global and slot-owned (no register/unregister lifecycle), slot occupancy is a structural
    /// container-tree check rather than a flow simulation.
    /// </summary>
    [PublicAPI]
    public static class StoryGraphValidator
    {
        private static readonly Regex _VarRef = new Regex(@"<var=([^<>]+?)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Validates the whole story. Pass <paramref name="localization"/> to additionally check inline
        /// <c>&lt;var=Name&gt;</c> text references against the project's variables.
        /// </summary>
        public static StoryValidationResult Validate(StoryProject project, LocProject? localization = null)
        {
            List<StoryProblem> problems = new();

            CheckDanglingOutputs(project, problems);
            CheckChoices(project, problems);
            CheckConditionFlows(project, problems);
            CheckGamebookText(project, localization, problems);

            foreach (StoryLogicNode logic in project.LogicNodes.Values)
            {
                CheckRandomizedInstruction(project, logic, problems);
                CheckTextReferences(project, logic, localization, problems);
            }

            CheckContainerSlots(project, problems);

            return new StoryValidationResult(problems);
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

                // The Else-fallback + per-choice-condition rules only apply to Automatic Choice. Choice Visibility (and
                // Hub Paths, which is always visibility-driven) has no Else and treats conditions as optional.
                bool varsWired    = FromInto(logic, logic.ExitVariablesIn.Id) != Guid.Empty;
                StoryExitAutoMode mode = logic.ExitMode == StoryLogicExitMode.HubPaths ? StoryExitAutoMode.ChoiceVisibility : logic.ExitAutoMode;
                if (varsWired && mode == StoryExitAutoMode.AutomaticChoice)
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
        /// a <b>constant</b> source, so the branch resolves the same in the App and the printed Gamebook.
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
            if (logic.GetVariableNodes.Find(n => n.OutPoint.Id == src) is StoryGetVariableNode gv)
            {
                StoryVariable? v = StoryLogicFlow.GetTarget(project, gv);
                return v is not null && (StoryBuiltInVariables.IsBuiltIn(v.Id) || StoryVariableValues.IsConstant(v));
            }
            // Prev Exit / incoming declared variables are exposed as constants inside the node.
            return StorySelectionResolver.IncomingVariables(project, logic).Exists(d => d.Id == src);
        }

        /// <summary>Reports SmartFormat render failures in each node's Gamebook text (e.g. a live value unavailable in the Gamebook).</summary>
        private static void CheckGamebookText(StoryProject project, LocProject? localization, List<StoryProblem> problems)
        {
            foreach (StoryLogicNode logic in project.LogicNodes.Values)
            {
                StoryGamebookPreview.Result gamebook = StoryGamebookPreview.Build(project, localization, logic);
                HashSet<string>             seen     = new();
                foreach (StoryGamebookPreview.Section section in gamebook.Sections)
                    foreach (string error in section.Rendered.Errors)
                        if (seen.Add(error))
                            problems.Add(Node(logic, UiLang.T(Localization.Validation.gamebookTextPrefix, new Dictionary<string, object> { ["node"] = NodeName(logic.Name), ["error"] = error })));
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
        /// Flags any inline <c>&lt;var=Name&gt;</c> reference in this node's localized text that doesn't name a variable
        /// in the project catalog (or a built-in). Variables are global now, so any catalogued name is valid anywhere.
        /// </summary>
        private static void CheckTextReferences(
            StoryProject project, StoryLogicNode logic, LocProject? localization, List<StoryProblem> problems)
        {
            if (localization is null || logic.LocalizationNodes.Count == 0) return;

            HashSet<string> known = new(StringComparer.OrdinalIgnoreCase);
            foreach (StoryVariable v in project.Variables.Values)
                if (!string.IsNullOrWhiteSpace(v.Name)) known.Add(v.Name);
            foreach (StoryVariable v in StoryBuiltInVariables.All)
                known.Add(v.Name);

            HashSet<string> reported = new(StringComparer.OrdinalIgnoreCase);
            foreach (StoryLocalizationNode loc in logic.LocalizationNodes)
            {
                string text = StoryLogicRenderer.LocalizedText(localization, loc.SelectedKeyId);
                if (text.Length == 0) continue;

                foreach (Match m in _VarRef.Matches(text))
                {
                    string name = m.Groups[1].Value.Trim();
                    if (known.Contains(name) || !reported.Add(name)) continue;
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

        // ── Container slot reservations ────────────────────────────────────────

        /// <summary>
        /// Checks that no physical slot is reserved by two variables on the same nesting path. Scenario-lifespan Internal
        /// variables reserve their slot for the whole story (exclusive); each container reserves the slots of the
        /// Chapter-lifespan variables in its <see cref="StoryContainerNode.UsedVariables"/>, and those may not collide
        /// with a scenario reservation, an ancestor container's reservation, or a sibling reservation in the same
        /// container. (Chapter variables in unrelated container subtrees may freely reuse a slot.)
        /// </summary>
        private static void CheckContainerSlots(StoryProject project, List<StoryProblem> problems)
        {
            // Scenario reservations — global and exclusive. Two scenario variables sharing a slot is itself an error.
            Dictionary<(StorageVariableType, int), StoryVariable> scenarioSlots = new();
            foreach (StoryVariable v in project.Variables.Values)
            {
                if (v.Scope != StoryVariableScope.Internal) continue;
                if (v.SlotIndex < 0 || v.SlotIndex >= StorageSlots.Count(StoryVariableSlots.Bank(v.InternalSubtype)))
                    problems.Add(VariableSlotProblem(v, Localization.Validation.slotOutOfRange));
                if (v.Lifespan != StoryVariableLifespan.Scenario) continue;

                (StorageVariableType, int) key = (StoryVariableSlots.Bank(v.InternalSubtype), v.SlotIndex);
                if (scenarioSlots.ContainsKey(key))
                    problems.Add(VariableSlotProblem(v, Localization.Validation.slotInUse));
                else
                    scenarioSlots[key] = v;
            }

            // Per-container chapter reservations.
            foreach (StoryContainerNode container in project.ContainerNodes.Values)
            {
                HashSet<(StorageVariableType, int)> ancestorSlots = AncestorReservedSlots(project, container);
                HashSet<(StorageVariableType, int)> own           = new();

                foreach (Guid id in container.UsedVariables)
                {
                    if (!project.Variables.TryGetValue(id, out StoryVariable? v) || v.Scope != StoryVariableScope.Internal) continue;
                    (StorageVariableType, int) key = (StoryVariableSlots.Bank(v.InternalSubtype), v.SlotIndex);

                    if (scenarioSlots.ContainsKey(key) || ancestorSlots.Contains(key) || !own.Add(key))
                        problems.Add(new StoryProblem
                        {
                            Severity    = StoryProblemSeverity.Error,
                            Message     = UiLang.T(Localization.Validation.slotInUse, new Dictionary<string, object> { ["slot"] = StoryVariableSlots.Label(v.InternalSubtype, v.SlotIndex) }),
                            ContainerId = container.Id
                        });
                }
            }
        }

        /// <summary>The slots reserved by every ancestor container's chapter <see cref="StoryContainerNode.UsedVariables"/>.</summary>
        private static HashSet<(StorageVariableType, int)> AncestorReservedSlots(StoryProject project, StoryContainerNode container)
        {
            HashSet<(StorageVariableType, int)> slots = new();
            Guid parentId = container.ParentContainer;
            int  guard    = 0;
            while (parentId != Guid.Empty && guard++ < 256 && project.ContainerNodes.TryGetValue(parentId, out StoryContainerNode? parent))
            {
                foreach (Guid id in parent.UsedVariables)
                    if (project.Variables.TryGetValue(id, out StoryVariable? v) && v.Scope == StoryVariableScope.Internal)
                        slots.Add((StoryVariableSlots.Bank(v.InternalSubtype), v.SlotIndex));
                parentId = parent.ParentContainer;
            }
            return slots;
        }

        private static StoryProblem VariableSlotProblem(StoryVariable v, Guid key) => new()
        {
            Severity = StoryProblemSeverity.Error,
            Message  = UiLang.T(key, new Dictionary<string, object>
            {
                ["slot"] = StoryVariableSlots.Label(v.InternalSubtype, v.SlotIndex),
                ["type"] = StoryVariableSlots.Bank(v.InternalSubtype)
            })
        };

        // ── Helpers ────────────────────────────────────────────────────────────

        private static Guid FromInto(StoryLogicNode logic, Guid toPoint) =>
            logic.ContentConnections.Find(c => c.ToPoint == toPoint)?.FromPoint ?? Guid.Empty;

        private static StoryProblem Node(StoryLogicNode logic, string message) => new()
        {
            Severity    = StoryProblemSeverity.Error,
            Message     = message,
            ContainerId = logic.ParentContainer,
            LogicNodeId = logic.Id
        };

        private static StoryProblem Inner(StoryLogicNode logic, Guid innerId, string message) => new()
        {
            Severity    = StoryProblemSeverity.Error,
            Message     = message,
            ContainerId = logic.ParentContainer,
            LogicNodeId = logic.Id,
            InnerNodeId = innerId
        };

        private static string NodeName(string name) => string.IsNullOrWhiteSpace(name) ? UiLang.T(Localization.Common.Placeholders.unnamed) : name;

        private static string PointName(StoryConnectionPoint point, string fallback) =>
            string.IsNullOrWhiteSpace(point.Name) ? fallback : point.Name;
    }

    /// <summary>The outcome of <see cref="StoryGraphValidator.Validate"/>: the problems found.</summary>
    public sealed class StoryValidationResult
    {
        public StoryValidationResult(List<StoryProblem> problems) => Problems = problems;

        public List<StoryProblem> Problems { get; }
    }
}
