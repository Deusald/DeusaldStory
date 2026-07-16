using System;
using System.Collections.Generic;
using System.Linq;
using DeusaldLocalizerCommon;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Builds the Gamebook preview of a single logic node. Sections are dimensioned only by the <b>previous</b> node's
    /// choices: a node reached over a plain Flow (or the story start) is one section; a node reached over a Single-path
    /// node's VFlow becomes one section per upstream choice, each pinning the incoming declared-variable constants. The
    /// node's own choices become the <b>continue-instructions</b> ("<i>{choice}</i> go to section …"). Global section
    /// numbers do not exist yet, so section references are placeholder tokens. Pure/host-agnostic for PDF export reuse.
    /// </summary>
    [PublicAPI]
    public static class StoryGamebookPreview
    {
        /// <summary>Hard cap on generated sections so a wide upstream choice list can't blow up the preview.</summary>
        public const int MAX_SECTIONS = 64;

        public sealed class Result
        {
            public List<Section>      Sections          { get; set; } = new();
            public List<ContinueLine> Continue          { get; set; } = new();
            public int                TotalCombinations { get; set; }
            public bool               Truncated         { get; set; }
        }

        /// <summary>One printed section — the node rendered for a single combination of incoming variable values.</summary>
        public sealed class Section
        {
            public string                           Label            { get; set; } = "";
            /// <summary>Stable per-combination reference token (see <see cref="SectionToken"/>) — how continue lines point here.</summary>
            public string                           Key              { get; set; } = "";
            public StoryLogicRenderer.RenderedLogic Rendered         { get; set; } = new();
            public bool                             IsInstructions   { get; set; }
            public List<string>                     InstructionLines { get; set; } = new();
        }

        /// <summary>One continue-instruction line at the bottom of every section (shared across a node's sections).</summary>
        public sealed class ContinueLine
        {
            public string Text    { get; set; } = "";
            public bool   IsError { get; set; }
            /// <summary>The logic node this line leads to, or <see cref="Guid.Empty"/> for End/error lines (not navigable).</summary>
            public Guid   TargetLogicId    { get; set; }
            /// <summary>The <see cref="Section.Key"/> of the specific target section this line points to (empty when not navigable).</summary>
            public string TargetSectionKey { get; set; } = "";
        }

        public static Result Build(StoryProject project, LocProject? localization, StoryLogicNode logic)
        {
            (List<StoryDeclaredVariable> incoming, List<Dictionary<Guid, string>> valueMaps, int total) = SectionsData(project, logic);

            List<Section> sections = valueMaps
                                    .Select(values => BuildSection(project, localization, logic, incoming, values))
                                    .ToList();

            return new Result
            {
                Sections          = sections,
                Continue          = BuildContinueLines(project, localization, logic),
                TotalCombinations = total,
                Truncated         = total > valueMaps.Count
            };
        }

        // ── Sections ─────────────────────────────────────────────────────────────

        private static Section BuildSection(
            StoryProject project, LocProject? localization, StoryLogicNode logic,
            List<StoryDeclaredVariable> incoming, Dictionary<Guid, string> values)
        {
            StoryLogicRenderer.RenderedLogic rendered = StoryLogicRenderer.Render(project, localization, logic, values, paper: true, StoryRenderTarget.Gamebook);
            string                           label    = ComboLabel(incoming, values);
            string                           key      = SectionToken(logic, incoming, values);

            if (!logic.GamebookInstructions)
                return new Section { Label = label, Key = key, Rendered = rendered };

            return new Section
            {
                Label            = label,
                Key              = key,
                Rendered         = rendered,
                IsInstructions   = true,
                InstructionLines = incoming.Select(dv => $"{(string.IsNullOrWhiteSpace(dv.Name) ? UiLang.T(Localization.Services.Gamebook.instructionVariableFallback) : dv.Name)} = {(values.TryGetValue(dv.Id, out string? v) ? v : "")}").ToList()
            };
        }

        /// <summary>
        /// The incoming declared variables and the per-section value maps for <paramref name="node"/>: the cartesian
        /// product of the upstream Single-path node's declared-variable possible values (one section per combination),
        /// or a single empty map when the node isn't reached over a Single-path VFlow. <paramref name="total"/> is the
        /// un-capped product size.
        /// </summary>
        private static (List<StoryDeclaredVariable> Incoming, List<Dictionary<Guid, string>> ValueMaps, int Total) SectionsData(
            StoryProject project, StoryLogicNode node)
        {
            StoryLogicNode? source = StorySelectionResolver.SourceNode(project, node);
            if (source is null || source.DeclaredVariables.Count == 0)
                return (new List<StoryDeclaredVariable>(), new List<Dictionary<Guid, string>> { new() }, 1);

            List<Dictionary<Guid, string>> maps = Combinations(source.DeclaredVariables, out int total);
            return (source.DeclaredVariables, maps, total);
        }

        /// <summary>The cartesian product of the declared variables' possible values as declared-var-id → value maps, capped at <see cref="MAX_SECTIONS"/>.</summary>
        private static List<Dictionary<Guid, string>> Combinations(List<StoryDeclaredVariable> vars, out int total)
        {
            List<Dictionary<Guid, string>> combos = new() { new() };
            total = 1;
            foreach (StoryDeclaredVariable v in vars)
            {
                List<string> possible = v.PossibleValues.Count > 0 ? v.PossibleValues : new List<string> { "" };
                total *= possible.Count;

                List<Dictionary<Guid, string>> next = new();
                foreach (Dictionary<Guid, string> baseCombo in combos)
                    foreach (string value in possible)
                    {
                        if (next.Count >= MAX_SECTIONS) break;
                        next.Add(new Dictionary<Guid, string>(baseCombo) { [v.Id] = value });
                    }
                combos = next;
            }
            return combos;
        }

        // ── Continue instructions ────────────────────────────────────────────────

        private static List<ContinueLine> BuildContinueLines(StoryProject project, LocProject? localization, StoryLogicNode logic)
        {
            List<ContinueLine> lines = new();
            List<StoryLogicRenderer.RenderedChoice> choices =
                StoryLogicRenderer.Choices(project, localization, logic, new Dictionary<Guid, string>(), StoryRenderTarget.Gamebook);

            if (choices.Count == 0)
            {
                lines.Add(new ContinueLine { Text = UiLang.T(Localization.Services.Gamebook.noChoices), IsError = true });
                return lines;
            }

            foreach (StoryLogicRenderer.RenderedChoice rc in choices)
            {
                bool   hasText = !string.IsNullOrWhiteSpace(rc.Text);
                string label   = hasText ? rc.Text : "";

                if (rc.OuterFlowOut == Guid.Empty)
                {
                    lines.Add(new ContinueLine { Text = UiLang.T(Localization.Services.Gamebook.choiceNotConnected, new Dictionary<string, object> { ["label"] = hasText ? label : "?" }), IsError = true });
                    continue;
                }

                StoryFlowNavigator.NextLogicResult next = StoryFlowNavigator.ResolveNextLogic(project, rc.OuterFlowOut);
                switch (next.Kind)
                {
                    case StoryFlowNavigator.NextKind.End:
                        string theEnd = StoryCommonLocalizationKeys.Resolve(localization, StoryCommonLocalizationKeys.GamebookTheEnd);
                        lines.Add(new ContinueLine { Text = hasText ? UiLang.T(Localization.Services.Gamebook.choiceToEnd, new Dictionary<string, object> { ["label"] = label, ["theEnd"] = theEnd }) : theEnd });
                        break;

                    case StoryFlowNavigator.NextKind.Dangling:
                        lines.Add(new ContinueLine { Text = UiLang.T(Localization.Services.Gamebook.choiceLeadsNowhere, new Dictionary<string, object> { ["label"] = hasText ? label : "?" }), IsError = true });
                        break;

                    case StoryFlowNavigator.NextKind.Logic when next.Logic is not null:
                        lines.AddRange(ChoiceToNode(project, localization, logic, rc, label, hasText, next.Logic));
                        break;
                }
            }

            return lines;
        }

        /// <summary>
        /// The continue line(s) for a choice leading to <paramref name="next"/>. When this node is Single-path and
        /// feeds <paramref name="next"/>'s variables, this choice maps to exactly one of <paramref name="next"/>'s
        /// sections (the one pinning this choice's values); otherwise a line per section of <paramref name="next"/>.
        /// </summary>
        private static IEnumerable<ContinueLine> ChoiceToNode(
            StoryProject project, LocProject? localization, StoryLogicNode logic,
            StoryLogicRenderer.RenderedChoice rc, string choiceText, bool hasText, StoryLogicNode next)
        {
            (List<StoryDeclaredVariable> incoming, List<Dictionary<Guid, string>> maps, _) = SectionsData(project, next);

            if (logic.ExitMode == StoryLogicExitMode.SinglePath
                && StorySelectionResolver.SourceNode(project, next) is StoryLogicNode src && src.Id == logic.Id
                && logic.Choices.Find(c => c.Id == rc.ChoiceId) is StoryChoice thisChoice)
            {
                Dictionary<Guid, string> values = StorySelectionResolver.ValuesForChoice(logic, thisChoice);
                yield return ContinueTo(localization, choiceText, hasText, next, SectionToken(next, incoming, values));
                yield break;
            }

            foreach (Dictionary<Guid, string> map in maps)
                yield return ContinueTo(localization, choiceText, hasText, next, SectionToken(next, incoming, map));
        }

        private static ContinueLine ContinueTo(LocProject? localization, string choiceText, bool hasText, StoryLogicNode next, string section) =>
            new()
            {
                Text = hasText
                    ? StoryCommonLocalizationKeys.Resolve(localization, StoryCommonLocalizationKeys.GamebookChoiceToSection,
                        new Dictionary<string, object> { ["choice"] = choiceText, ["section"] = section })
                    : StoryCommonLocalizationKeys.Resolve(localization, StoryCommonLocalizationKeys.GamebookContinueToSection,
                        new Dictionary<string, object> { ["section"] = section }),
                TargetLogicId    = next.Id,
                TargetSectionKey = section
            };

        // ── Section tokens ─────────────────────────────────────────────────────────

        /// <summary>Placeholder section reference token, e.g. <c>[§ Forest Path · A=Win]</c>.</summary>
        private static string SectionToken(StoryLogicNode node, List<StoryDeclaredVariable> vars, Dictionary<Guid, string> values)
        {
            string name    = string.IsNullOrWhiteSpace(node.Name) ? UiLang.T(Localization.Common.Placeholders.unnamed) : node.Name;
            string summary = ComboLabel(vars, values);
            return string.IsNullOrEmpty(summary)
                ? UiLang.T(Localization.Services.Gamebook.sectionTokenBare, new Dictionary<string, object> { ["name"] = name })
                : UiLang.T(Localization.Services.Gamebook.sectionToken,     new Dictionary<string, object> { ["name"] = name, ["summary"] = summary });
        }

        /// <summary>A short "A=val, B=val2" label for a combination (empty when no incoming variables).</summary>
        private static string ComboLabel(List<StoryDeclaredVariable> vars, Dictionary<Guid, string> values) =>
            string.Join(", ", vars.Select(v => $"{v.Name}={(values.TryGetValue(v.Id, out string? val) ? val : "")}"));
    }
}
