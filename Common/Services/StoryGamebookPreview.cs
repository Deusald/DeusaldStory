using System;
using System.Collections.Generic;
using System.Linq;
using DeusaldLocalizerCommon;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Builds the Gamebook preview of a single logic node. Sections are dimensioned only by the <b>previous</b> node's
    /// choice definitions: a node reached over a plain flow (or the story start) is one section; a node reached from a
    /// Single-path node becomes one section per combination of that node's choices, each pinning the ChoiceA/B/C values
    /// (and any real variable those choices fix). The node's own continuations become the <b>continue-instructions</b>
    /// ("<i>{choice}</i> go to section …"). Global section numbers do not exist yet, so section references are
    /// placeholder tokens. Pure/host-agnostic for PDF export reuse.
    /// </summary>
    [PublicAPI]
    public static class StoryGamebookPreview
    {
        /// <summary>Hard cap on generated sections so a wide upstream choice list can't blow up the preview.</summary>
        public const int MAX_SECTIONS = 64;

        public sealed class Result
        {
            public List<Section> Sections          { get; set; } = new();
            public int           TotalCombinations { get; set; }
            public bool          Truncated         { get; set; }
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
            /// <summary>The composed "go to section …" phrase and navigation target for each inline <c>&lt;choice=Name&gt;</c> in the text, keyed by name (see <see cref="BuildInlineLinks"/>).</summary>
            public List<InlineLink>                 InlineLinks      { get; set; } = new();
            /// <summary>This section's own continue-instruction lines. Per-section because Choice Visibility gates each option against the incoming variable values pinned in <see cref="Values"/> — different sections can offer different continuations. Empty when the text carries inline <c>&lt;choice&gt;</c> links (they replace the standalone lines).</summary>
            public List<ContinueLine>               Continue         { get; set; } = new();
            /// <summary>The variable values the incoming choice pinned for this section (variable id → value); the basis for its per-section Choice-Visibility filtering and for printing those variables concretely.</summary>
            public Dictionary<Guid, string>         Values           { get; set; } = new();
        }

        /// <summary>
        /// An inline <c>&lt;choice=Name&gt;</c> reference resolved for the Gamebook: the composed "<i>{choice}</i> go to
        /// section …" phrase the tag expands to, plus — when it leads to a real section — the navigation target so the
        /// preview can render it as a clickable link (like a continue line). End / unwired / unresolved references carry
        /// only text (<see cref="TargetLogicId"/> stays <see cref="Guid.Empty"/>, so they render as plain phrases).
        /// </summary>
        public sealed class InlineLink
        {
            /// <summary>The name written in the tag — how a text occurrence maps back to this entry (case-insensitive).</summary>
            public string Name             { get; set; } = "";
            public string Text             { get; set; } = "";
            public Guid   TargetLogicId    { get; set; }
            public string TargetSectionKey { get; set; } = "";
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
            SectionsInfo info = SectionsData(project, logic);

            List<Section> sections = info.Combos
                                         .Select(combo => BuildSection(project, localization, logic, info.Source, combo))
                                         .ToList();

            // When the text references choices inline (<choice=Name>), those links replace the standalone continue lines
            // — a whole-node decision (matching the App), so one inline section suppresses the standalone lines everywhere.
            // Otherwise each section builds its own lines against its pinned values, so Choice Visibility can hide the
            // options a section's incoming variables rule out (App parity — previously the Gamebook showed them all).
            if (!sections.Exists(s => s.Rendered.InlineChoices.Count > 0))
                foreach (Section section in sections)
                    section.Continue = BuildContinueLines(project, localization, logic, section.Values);

            return new Result
            {
                Sections          = sections,
                TotalCombinations = info.Total,
                Truncated         = info.Total > info.Combos.Count
            };
        }

        // ── Sections ─────────────────────────────────────────────────────────────

        private static Section BuildSection(
            StoryProject project, LocProject? localization, StoryLogicNode logic,
            StoryLogicNode? source, StoryChoiceSources.Combination combo)
        {
            // The pinned values are both the live values this section renders against and the set the dictionary
            // prints concretely instead of as slot pills — the player turned to this section by making those choices.
            StoryLogicRenderer.RenderedLogic rendered = StoryLogicRenderer.Render(
                project, localization, logic, combo.Pins, paper: true, StoryRenderTarget.Gamebook, pinned: combo.Pins);

            List<InlineLink> inline = BuildInlineLinks(project, localization, rendered);
            string           label  = ComboLabel(project, source, combo);
            string           key    = SectionToken(project, logic, source, combo);

            if (!logic.GamebookInstructions)
                return new Section { Label = label, Key = key, Rendered = rendered, InlineLinks = inline, Values = combo.Pins };

            return new Section
            {
                Label            = label,
                Key              = key,
                Rendered         = rendered,
                InlineLinks      = inline,
                Values           = combo.Pins,
                IsInstructions   = true,
                InstructionLines = DimensionLabels(project, source, combo)
            };
        }

        /// <summary>The upstream node dimensioning a node's sections, the capped combinations, and the un-capped total.</summary>
        private sealed class SectionsInfo
        {
            public StoryLogicNode?                      Source { get; set; }
            public List<StoryChoiceSources.Combination> Combos { get; set; } = new();
            public int                                  Total  { get; set; }
        }

        /// <summary>
        /// The sections of <paramref name="node"/>: one per combination of the upstream Single-path node's choice
        /// definitions, or a single empty combination when nothing upstream dimensions it.
        /// </summary>
        private static SectionsInfo SectionsData(StoryProject project, StoryLogicNode node)
        {
            StoryLogicNode? source = StoryChoiceSources.SourceOf(project, node);
            if (source is null || source.ChoiceDefinitions.Count == 0)
                return new SectionsInfo { Source = null, Combos = new List<StoryChoiceSources.Combination> { new() }, Total = 1 };

            List<StoryChoiceSources.Combination> combos = StoryChoiceSources.Combinations(project, source, MAX_SECTIONS, out int total);
            return new SectionsInfo { Source = source, Combos = combos, Total = total };
        }

        /// <summary>
        /// The name each choice dimension reads under — the definition's target variable, falling back to the Choice
        /// variable it writes (ChoiceA/B/C) when the definition targets no single variable (a range or option list).
        /// </summary>
        private static string DimensionName(StoryProject project, StoryChoiceDefinition def, int index)
        {
            if (def.Kind == StoryChoiceDefKind.Variable
                && StoryVariableCatalog.Resolve(project, def.SelectedVariableId) is StoryVariable target
                && !string.IsNullOrWhiteSpace(target.Name))
                return target.Name;

            return StoryChoiceVariables.ForIndex(index)?.Name ?? UiLang.T(Localization.Services.Gamebook.instructionVariableFallback);
        }

        /// <summary>One "Name = value" line per choice dimension this section was reached through.</summary>
        private static List<string> DimensionLabels(StoryProject project, StoryLogicNode? source, StoryChoiceSources.Combination combo)
        {
            List<string> lines = new();
            if (source is null) return lines;

            for (int x = 0; x < source.ChoiceDefinitions.Count && x < combo.Entries.Count; ++x)
            {
                string name  = DimensionName(project, source.ChoiceDefinitions[x], x);
                string value = combo.Entries[x].ChoiceValue;
                lines.Add($"{name} = {value}");
            }
            return lines;
        }

        // ── Continue instructions ────────────────────────────────────────────────

        private static List<ContinueLine> BuildContinueLines(StoryProject project, LocProject? localization, StoryLogicNode logic, IReadOnlyDictionary<Guid, string> values)
        {
            List<ContinueLine> lines = new();
            List<StoryLogicRenderer.RenderedChoice> choices =
                StoryLogicRenderer.Choices(project, localization, logic, values, StoryRenderTarget.Gamebook);

            if (choices.Count == 0)
            {
                lines.Add(new ContinueLine { Text = UiLang.T(Localization.Services.Gamebook.noChoices), IsError = true });
                return lines;
            }

            // Hub Paths — destinations are presented as Hub Cards to gather, not sections to jump to.
            if (logic.ExitMode == StoryLogicExitMode.HubPaths)
                return BuildHubCardLines(project, localization, choices);

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
        /// Hub Paths continue lines: one "Gather Hub Card …" line per destination — each on its own line and prefixed
        /// with the choice's text (it may carry the instruction that decides whether the card is gathered) — plus an
        /// error line for any unwired / dangling choice. An End destination lists as THE END. Real per-card numbering
        /// awaits the not-yet-built global section numbering / PDF export.
        /// </summary>
        private static List<ContinueLine> BuildHubCardLines(StoryProject project, LocProject? localization, List<StoryLogicRenderer.RenderedChoice> choices)
        {
            List<ContinueLine> lines = new();
            foreach (StoryLogicRenderer.RenderedChoice rc in choices)
            {
                bool hasText = !string.IsNullOrWhiteSpace(rc.Text);
                if (rc.OuterFlowOut == Guid.Empty)
                {
                    lines.Add(new ContinueLine { Text = UiLang.T(Localization.Services.Gamebook.choiceNotConnected, new Dictionary<string, object> { ["label"] = hasText ? rc.Text : "?" }), IsError = true });
                    continue;
                }
                StoryFlowNavigator.NextLogicResult next = StoryFlowNavigator.ResolveNextLogic(project, rc.OuterFlowOut);
                switch (next.Kind)
                {
                    case StoryFlowNavigator.NextKind.Logic when next.Logic is not null:
                        string card = HubCardToken(next.Logic);
                        lines.Add(new ContinueLine
                        {
                            Text = hasText
                                ? StoryCommonLocalizationKeys.Resolve(localization, StoryCommonLocalizationKeys.GamebookGatherHubCardChoice,
                                    new Dictionary<string, object> { ["choice"] = rc.Text, ["card"] = card })
                                : StoryCommonLocalizationKeys.Resolve(localization, StoryCommonLocalizationKeys.GamebookGatherHubCard,
                                    new Dictionary<string, object> { ["card"] = card }),
                            TargetLogicId = next.Logic.Id
                        });
                        break;

                    case StoryFlowNavigator.NextKind.End:
                        string theEnd = StoryCommonLocalizationKeys.Resolve(localization, StoryCommonLocalizationKeys.GamebookTheEnd);
                        lines.Add(new ContinueLine { Text = hasText ? UiLang.T(Localization.Services.Gamebook.choiceToEnd, new Dictionary<string, object> { ["label"] = rc.Text, ["theEnd"] = theEnd }) : theEnd });
                        break;

                    case StoryFlowNavigator.NextKind.Dangling:
                        lines.Add(new ContinueLine { Text = UiLang.T(Localization.Services.Gamebook.choiceLeadsNowhere, new Dictionary<string, object> { ["label"] = hasText ? rc.Text : "?" }), IsError = true });
                        break;
                }
            }
            return lines;
        }

        /// <summary>Placeholder Hub Card reference token, e.g. <c>[Hub Card · Forest Path]</c>.</summary>
        private static string HubCardToken(StoryLogicNode node)
        {
            string name = string.IsNullOrWhiteSpace(node.Name) ? UiLang.T(Localization.Common.Placeholders.unnamed) : node.Name;
            return UiLang.T(Localization.Services.Gamebook.hubCardToken, new Dictionary<string, object> { ["name"] = name });
        }

        /// <summary>
        /// Resolves each inline <c>&lt;choice=Name&gt;</c> reference into the "go to section …" phrase it expands to
        /// (prefixed with the choice's own text when it has any, mirroring the standalone continue line) and, when it
        /// leads to a real section, the navigation target that lets the preview make it a clickable link. The raw tags
        /// stay in the block text — the host splices these links in at render time (Gamebook only). An unresolved name
        /// yields no link, so its tag is left visible.
        /// </summary>
        private static List<InlineLink> BuildInlineLinks(StoryProject project, LocProject? localization, StoryLogicRenderer.RenderedLogic rendered)
        {
            List<InlineLink> links = new();
            foreach (StoryLogicRenderer.RenderedInlineChoice ic in rendered.InlineChoices)
            {
                bool hasText = !string.IsNullOrWhiteSpace(ic.Text);
                if (ic.OuterFlowOut == Guid.Empty)
                {
                    links.Add(new InlineLink { Name = ic.Name, Text = ic.Text }); // unwired — just the label, not navigable
                    continue;
                }

                StoryFlowNavigator.NextLogicResult next = StoryFlowNavigator.ResolveNextLogic(project, ic.OuterFlowOut);
                switch (next.Kind)
                {
                    case StoryFlowNavigator.NextKind.Logic when next.Logic is not null:
                        string section = SectionToken(project, next.Logic, null, new StoryChoiceSources.Combination());
                        string text    = hasText
                            ? StoryCommonLocalizationKeys.Resolve(localization, StoryCommonLocalizationKeys.GamebookChoiceToSection,
                                new Dictionary<string, object> { ["choice"] = ic.Text, ["section"] = section })
                            : StoryCommonLocalizationKeys.Resolve(localization, StoryCommonLocalizationKeys.GamebookGoToSection,
                                new Dictionary<string, object> { ["section"] = section });
                        links.Add(new InlineLink { Name = ic.Name, Text = text, TargetLogicId = next.Logic.Id, TargetSectionKey = section });
                        break;

                    case StoryFlowNavigator.NextKind.End:
                        string endText = hasText
                            ? UiLang.T(Localization.Services.Gamebook.choiceToEnd, new Dictionary<string, object> { ["label"] = ic.Text, ["theEnd"] = StoryCommonLocalizationKeys.Resolve(localization, StoryCommonLocalizationKeys.GamebookTheEnd) })
                            : StoryCommonLocalizationKeys.Resolve(localization, StoryCommonLocalizationKeys.GamebookTheEnd);
                        links.Add(new InlineLink { Name = ic.Name, Text = endText }); // End — no section to navigate to
                        break;

                    default:
                        links.Add(new InlineLink { Name = ic.Name, Text = ic.Text });
                        break;
                }
            }
            return links;
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
            SectionsInfo info = SectionsData(project, next);

            // This node feeds the next one's sections, so the continuation the player took picks exactly one of them —
            // the combination agreeing with everything this choice pinned.
            if (info.Source is not null && info.Source.Id == logic.Id && rc.Pins.Count > 0)
            {
                StoryChoiceSources.Combination? match = info.Combos.Find(c => AgreesWith(c.Pins, rc.Pins));
                if (match is not null)
                {
                    yield return ContinueTo(localization, choiceText, hasText, next, SectionToken(project, next, info.Source, match));
                    yield break;
                }
            }

            foreach (StoryChoiceSources.Combination combo in info.Combos)
                yield return ContinueTo(localization, choiceText, hasText, next, SectionToken(project, next, info.Source, combo));
        }

        /// <summary>Whether every key the choice pinned is present in the section's pins with the same value.</summary>
        private static bool AgreesWith(IReadOnlyDictionary<Guid, string> sectionPins, IReadOnlyDictionary<Guid, string> choicePins)
        {
            foreach (KeyValuePair<Guid, string> pin in choicePins)
                if (!sectionPins.TryGetValue(pin.Key, out string? value) || !string.Equals(value, pin.Value, StringComparison.Ordinal))
                    return false;
            return true;
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

        /// <summary>Placeholder section reference token, e.g. <c>[§ Forest Path · Health=3]</c>.</summary>
        private static string SectionToken(StoryProject project, StoryLogicNode node, StoryLogicNode? source, StoryChoiceSources.Combination combo)
        {
            string name    = string.IsNullOrWhiteSpace(node.Name) ? UiLang.T(Localization.Common.Placeholders.unnamed) : node.Name;
            string summary = ComboLabel(project, source, combo);
            return string.IsNullOrEmpty(summary)
                ? UiLang.T(Localization.Services.Gamebook.sectionTokenBare, new Dictionary<string, object> { ["name"] = name })
                : UiLang.T(Localization.Services.Gamebook.sectionToken,     new Dictionary<string, object> { ["name"] = name, ["summary"] = summary });
        }

        /// <summary>A short "Health=3, ChoiceB=Left" label for a combination (empty when nothing dimensions the node).</summary>
        private static string ComboLabel(StoryProject project, StoryLogicNode? source, StoryChoiceSources.Combination combo)
        {
            if (source is null || combo.Entries.Count == 0) return "";

            List<string> parts = new();
            for (int x = 0; x < source.ChoiceDefinitions.Count && x < combo.Entries.Count; ++x)
                parts.Add($"{DimensionName(project, source.ChoiceDefinitions[x], x)}={combo.Entries[x].ChoiceValue}");
            return string.Join(", ", parts);
        }
    }
}
