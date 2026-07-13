using System;
using System.Collections.Generic;
using System.Linq;
using DeusaldLocalizerCommon;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Builds the Gamebook preview of a single logic node: every combination of the node's External Variable values
    /// becomes a printed <b>section</b>, and the node's exits become example <b>continue-instructions</b> that look
    /// at the next node's own variable combinations ("To continue for two players go to section …"). Global section
    /// numbers do not exist yet, so section references are shown as placeholder tokens. Pure/host-agnostic so the
    /// eventual PDF export can reuse it.
    /// </summary>
    [PublicAPI]
    public static class StoryGamebookPreview
    {
        /// <summary>Hard cap on generated section variants so a wide variable product can't blow up the preview.</summary>
        public const int MAX_SECTIONS = 64;

        public sealed class Result
        {
            public List<Section>      Sections          { get; set; } = new();
            public List<ContinueLine> Continue          { get; set; } = new();
            public int                TotalCombinations { get; set; }
            public bool               Truncated         { get; set; }
        }

        /// <summary>One printed section — the node rendered for a single combination of variable values.</summary>
        public sealed class Section
        {
            public string                           Label            { get; set; } = "";
            public StoryLogicRenderer.RenderedLogic Rendered         { get; set; } = new();
            public bool                             IsInstructions   { get; set; }
            public List<string>                     InstructionLines { get; set; } = new();
        }

        /// <summary>One continue-instruction line at the bottom of every section (shared across a node's sections).</summary>
        public sealed class ContinueLine
        {
            public string Text    { get; set; } = "";
            public bool   IsError { get; set; }
        }

        public static Result Build(StoryProject project, LocProject? localization, StoryLogicNode logic)
        {
            List<StoryVariable>              vars  = OrderedVariables(project, logic);
            List<Dictionary<Guid, string>>   combos = Combinations(vars, out int total, out bool truncated);

            List<Section> sections = combos
                                    .Select(values => BuildSection(project, localization, logic, vars, values))
                                    .ToList();

            return new Result
            {
                Sections          = sections,
                Continue          = BuildContinueLines(project, localization, logic),
                TotalCombinations = total,
                Truncated         = truncated
            };
        }

        // ── Sections ─────────────────────────────────────────────────────────────

        private static Section BuildSection(
            StoryProject project, LocProject? localization, StoryLogicNode logic,
            List<StoryVariable> vars, Dictionary<Guid, string> values)
        {
            StoryLogicRenderer.RenderedLogic rendered = StoryLogicRenderer.Render(project, localization, logic, values, paper: true, StoryRenderTarget.Gamebook);
            string                           label    = ComboLabel(vars, values);

            if (!logic.GamebookInstructions)
                return new Section { Label = label, Rendered = rendered };

            return new Section
            {
                Label            = label,
                Rendered         = rendered,
                IsInstructions   = true,
                InstructionLines = InstructionLines(localization, vars, values)
            };
        }

        /// <summary>
        /// Generated player-facing instructions for a <see cref="StoryLogicNode.GamebookInstructions"/> section.
        /// Today the only operation source is the node's External Variables and their Gamebook condition keys; future
        /// interaction blocks (e.g. a dice-roll node) contribute their own baked keys the same way.
        /// </summary>
        private static List<string> InstructionLines(LocProject? localization, List<StoryVariable> vars, Dictionary<Guid, string> values)
        {
            List<string> lines = new();
            foreach (StoryVariable v in vars)
            {
                string value = values.TryGetValue(v.Id, out string? val) ? val : "";
                lines.Add(ConditionText(localization, v, value));
            }
            return lines;
        }

        // ── Continue instructions ────────────────────────────────────────────────

        private static List<ContinueLine> BuildContinueLines(StoryProject project, LocProject? localization, StoryLogicNode logic)
        {
            // A single-selection node funnels every branch into one flow-out; each branch instead pins the receiver's
            // Selection value and points to that specific section.
            if (logic.ExitMode == StoryLogicExitMode.SingleSelection)
                return BuildSelectionContinueLines(project, localization, logic);

            // When the node ends in a Choice, the choices *are* the continue instructions — each is a labelled
            // "go to section" line. Choice text is resolved once with empty values (the preview uses each variable's
            // first possible value); node-driven variable branching is still enumerated per next-node combo below.
            List<StoryLogicRenderer.RenderedChoice> choices =
                StoryLogicRenderer.Choices(project, localization, logic, new Dictionary<Guid, string>(), StoryRenderTarget.Gamebook);
            if (choices.Count > 0)
                return BuildChoiceLines(project, localization, choices);

            List<ContinueLine> lines = new();

            // Only the exits the Gamebook flow actually reaches — a node whose App/Gamebook flow splitter routes the
            // two mediums to different exits must not print the App-only exit's continuation here.
            List<Guid> reached = StoryLogicRenderer.ReachedExits(project, logic, StoryRenderTarget.Gamebook);

            foreach (StoryConnectionPoint exit in logic.ExitPoints)
            {
                if (!reached.Contains(exit.Id)) continue;

                StoryFlowNavigator.NextLogicResult next = StoryFlowNavigator.ResolveNextLogic(project, exit.Id);

                switch (next.Kind)
                {
                    case StoryFlowNavigator.NextKind.End:
                        lines.Add(new ContinueLine { Text = StoryCommonLocalizationKeys.Resolve(localization, StoryCommonLocalizationKeys.GamebookTheEnd) });
                        break;

                    case StoryFlowNavigator.NextKind.Dangling:
                        lines.Add(new ContinueLine
                        {
                            Text    = $"Exit “{(string.IsNullOrWhiteSpace(exit.Name) ? "?" : exit.Name)}” is not connected — every exit must lead somewhere.",
                            IsError = true
                        });
                        break;

                    case StoryFlowNavigator.NextKind.Logic when next.Logic is not null:
                        lines.AddRange(ContinueToNode(project, localization, next.Logic));
                        break;
                }
            }

            return lines;
        }

        // ── Choice instructions ──────────────────────────────────────────────────

        /// <summary>Builds the "<i>{choice}</i> go to section {section}" lines for a node whose spine ends in a Choice.</summary>
        private static List<ContinueLine> BuildChoiceLines(
            StoryProject project, LocProject? localization, List<StoryLogicRenderer.RenderedChoice> choices)
        {
            List<ContinueLine> lines = new();

            foreach (StoryLogicRenderer.RenderedChoice choice in choices)
            {
                string label = string.IsNullOrWhiteSpace(choice.Text) ? "(choice)" : choice.Text;

                if (choice.ExitPointId == Guid.Empty)
                {
                    lines.Add(new ContinueLine { Text = $"Choice “{label}” is not connected to an exit.", IsError = true });
                    continue;
                }

                StoryFlowNavigator.NextLogicResult next = StoryFlowNavigator.ResolveNextLogic(project, choice.ExitPointId);
                switch (next.Kind)
                {
                    case StoryFlowNavigator.NextKind.End:
                        lines.Add(new ContinueLine
                        {
                            Text = $"{label} — {StoryCommonLocalizationKeys.Resolve(localization, StoryCommonLocalizationKeys.GamebookTheEnd)}"
                        });
                        break;

                    case StoryFlowNavigator.NextKind.Dangling:
                        lines.Add(new ContinueLine { Text = $"Choice “{label}” leads to an exit that is not connected.", IsError = true });
                        break;

                    case StoryFlowNavigator.NextKind.Logic when next.Logic is not null:
                        lines.AddRange(ChoiceToNode(project, localization, label, next.Logic));
                        break;
                }
            }

            return lines;
        }

        /// <summary>Enumerates the choice target's variable combinations into one "<i>{choice}</i> go to section {section}" line each.</summary>
        private static IEnumerable<ContinueLine> ChoiceToNode(StoryProject project, LocProject? localization, string choiceText, StoryLogicNode next)
        {
            List<StoryVariable>            vars   = OrderedVariables(project, next);
            List<Dictionary<Guid, string>> combos = Combinations(vars, out _, out _);

            foreach (Dictionary<Guid, string> combo in combos)
            {
                string section = SectionToken(next, vars, combo);
                yield return new ContinueLine
                {
                    Text = StoryCommonLocalizationKeys.Resolve(localization, StoryCommonLocalizationKeys.GamebookChoiceToSection,
                        new Dictionary<string, object> { ["choice"] = choiceText, ["section"] = section })
                };
            }
        }

        // ── Single-selection continue instructions ───────────────────────────────

        /// <summary>
        /// Builds the continue instructions for a <see cref="StoryLogicExitMode.SingleSelection"/> node: every branch
        /// (its choices, or its bare exits) funnels through the one Selection flow-out to the same receiver, so each
        /// line points to the receiver section whose Selection is pinned to that branch's (remapped) exit value.
        /// </summary>
        private static List<ContinueLine> BuildSelectionContinueLines(StoryProject project, LocProject? localization, StoryLogicNode logic)
        {
            StoryFlowNavigator.NextLogicResult next = StoryFlowNavigator.ResolveNextLogic(project, logic.SelectionFlowOut.Id);

            if (next.Kind == StoryFlowNavigator.NextKind.Dangling)
                return new List<ContinueLine> { new() { Text = "The Selection flow-out is not connected — wire it to the node that reads the selection.", IsError = true } };

            // The player-facing branches: a spine-ending Choice's options, else the node's bare exits.
            List<StoryLogicRenderer.RenderedChoice> choices =
                StoryLogicRenderer.Choices(project, localization, logic, new Dictionary<Guid, string>(), StoryRenderTarget.Gamebook);
            List<(string Label, Guid ExitId)> branches = choices.Count > 0
                ? choices.Select(c => (c.Text, c.ExitPointId)).ToList()
                : logic.ExitPoints.Select(e => (e.Name, e.Id)).ToList();

            StoryLogicNode? receiver = next.Kind == StoryFlowNavigator.NextKind.Logic ? next.Logic : null;
            List<ContinueLine> lines = new();

            foreach ((string label, Guid exitId) in branches)
            {
                StoryConnectionPoint? exit = logic.ExitPoints.Find(e => e.Id == exitId);
                string exitName  = exit is null || string.IsNullOrWhiteSpace(exit.Name) ? "?" : exit.Name;
                string labelText = string.IsNullOrWhiteSpace(label) ? exitName : label;

                if (next.Kind == StoryFlowNavigator.NextKind.End)
                {
                    lines.Add(new ContinueLine { Text = $"{labelText} — {StoryCommonLocalizationKeys.Resolve(localization, StoryCommonLocalizationKeys.GamebookTheEnd)}" });
                    continue;
                }

                // The value the receiver's Selection takes for this exit (remapped when the receiver remaps).
                string selValue = receiver is { AcceptExitVariable: true } && exit is not null
                    ? StorySelectionResolver.ValueFor(receiver.PrevExitVariable, exit)
                    : "";

                lines.AddRange(SelectionToNode(project, localization, labelText, receiver!, selValue));
            }

            return lines;
        }

        /// <summary>
        /// Enumerates the receiver's <em>other</em> variable combinations (Selection pinned to <paramref name="selValue"/>)
        /// into one "<i>{label}</i> go to section {section}" line each, so the section tokens match the receiver's own.
        /// </summary>
        private static IEnumerable<ContinueLine> SelectionToNode(
            StoryProject project, LocProject? localization, string label, StoryLogicNode receiver, string selValue)
        {
            List<StoryVariable> fullVars  = OrderedVariables(project, receiver);           // includes the Selection variable
            Guid                selVarId  = receiver.PrevExitVariable.Id;
            List<StoryVariable> otherVars = fullVars.Where(v => v.Id != selVarId).ToList();
            List<Dictionary<Guid, string>> combos = Combinations(otherVars, out _, out _);

            foreach (Dictionary<Guid, string> combo in combos)
            {
                Dictionary<Guid, string> pinned = new(combo) { [selVarId] = selValue };
                string section = SectionToken(receiver, fullVars, pinned);
                yield return new ContinueLine
                {
                    Text = StoryCommonLocalizationKeys.Resolve(localization, StoryCommonLocalizationKeys.GamebookChoiceToSection,
                        new Dictionary<string, object> { ["choice"] = label, ["section"] = section })
                };
            }
        }

        /// <summary>Enumerates the next node's variable combinations into one continue line each (a placeholder section per combo).</summary>
        private static IEnumerable<ContinueLine> ContinueToNode(StoryProject project, LocProject? localization, StoryLogicNode next)
        {
            List<StoryVariable>            vars   = OrderedVariables(project, next);
            List<Dictionary<Guid, string>> combos = Combinations(vars, out _, out _);

            foreach (Dictionary<Guid, string> combo in combos)
            {
                string section   = SectionToken(next, vars, combo);
                string condition = string.Join(", ", vars.Select(v => ConditionText(localization, v, combo.TryGetValue(v.Id, out string? val) ? val : "")));

                string text = string.IsNullOrWhiteSpace(condition)
                    ? StoryCommonLocalizationKeys.Resolve(localization, StoryCommonLocalizationKeys.GamebookContinueToSection,
                        new Dictionary<string, object> { ["section"] = section })
                    : StoryCommonLocalizationKeys.Resolve(localization, StoryCommonLocalizationKeys.GamebookContinueConditional,
                        new Dictionary<string, object> { ["condition"] = condition, ["section"] = section });

                yield return new ContinueLine { Text = text };
            }
        }

        /// <summary>
        /// The condition text for <paramref name="v"/> at <paramref name="value"/>: its Gamebook condition key
        /// (SmartFormatted with the variable's name → value), or a plain "Name = value" fallback when unlinked.
        /// </summary>
        private static string ConditionText(LocProject? localization, StoryVariable v, string value)
        {
            if (v.ConditionKeyId != Guid.Empty
                && StoryLogicRenderer.LocalizedText(localization, v.ConditionKeyId) is { Length: > 0 } template)
            {
                Dictionary<string, object> vals = new(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(v.Name)) vals[v.Name] = value;
                return StoryConditionPreview.Render(template, vals, out _);
            }

            string name = string.IsNullOrWhiteSpace(v.Name) ? "(variable)" : v.Name;
            return $"{name} = {value}";
        }

        /// <summary>Placeholder section reference token, e.g. <c>[§ Forest Path · Players=2]</c>.</summary>
        private static string SectionToken(StoryLogicNode node, List<StoryVariable> vars, Dictionary<Guid, string> combo)
        {
            string name    = string.IsNullOrWhiteSpace(node.Name) ? "(unnamed)" : node.Name;
            string summary = ComboLabel(vars, combo);
            return string.IsNullOrEmpty(summary) ? $"[§ {name}]" : $"[§ {name} · {summary}]";
        }

        // ── Variables & combinations ─────────────────────────────────────────────

        /// <summary>
        /// The distinct story variables that dimension <paramref name="logic"/>'s Gamebook sections, ordered by name:
        /// the variables its External Variable nodes reference, plus — when the node accepts a wired-in exit variable —
        /// the synthetic Selection variable, so the node expands into one section per upstream exit value.
        /// </summary>
        private static List<StoryVariable> OrderedVariables(StoryProject project, StoryLogicNode logic)
        {
            Dictionary<Guid, StoryVariable> map = new();
            foreach (StoryExternalVariableNode n in logic.ExternalVariableNodes)
                if (n.SelectedVariableId != Guid.Empty && project.Variables.TryGetValue(n.SelectedVariableId, out StoryVariable? v))
                    map[v.Id] = v;

            List<StoryVariable> ordered = map.Values.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();

            // The Selection dimension goes last so its sections read "…, Selection=Win" after the story variables.
            if (StorySelectionResolver.SelectionVariable(project, logic) is StoryVariable selection)
                ordered.Add(selection);

            return ordered;
        }

        /// <summary>
        /// The cartesian product of <paramref name="vars"/>' possible values as variable-id → value maps. A variable
        /// with no possible values contributes a single empty value. <paramref name="total"/> is the full (un-capped)
        /// product size; the returned list is capped at <see cref="MAX_SECTIONS"/> with <paramref name="truncated"/> set.
        /// </summary>
        private static List<Dictionary<Guid, string>> Combinations(List<StoryVariable> vars, out int total, out bool truncated)
        {
            List<Dictionary<Guid, string>> combos = new() { new Dictionary<Guid, string>() };
            total = 1;

            foreach (StoryVariable v in vars)
            {
                List<string> possible = v.PossibleValues.Count > 0 ? v.PossibleValues : new List<string> { "" };
                total *= possible.Count;

                List<Dictionary<Guid, string>> next = new();
                foreach (Dictionary<Guid, string> baseCombo in combos)
                    foreach (string value in possible)
                    {
                        if (next.Count >= MAX_SECTIONS) break;
                        Dictionary<Guid, string> merged = new(baseCombo) { [v.Id] = value };
                        next.Add(merged);
                    }
                combos = next;
            }

            truncated = total > combos.Count;
            return combos;
        }

        /// <summary>A short "Var=val, Var2=val2" label for a combination (empty when no variables).</summary>
        private static string ComboLabel(List<StoryVariable> vars, Dictionary<Guid, string> values) =>
            string.Join(", ", vars.Select(v => $"{v.Name}={(values.TryGetValue(v.Id, out string? val) ? val : "")}"));
    }
}
