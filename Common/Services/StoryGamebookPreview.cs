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
            /// <summary>The composed "go to section …" phrase and navigation target for each inline <c>&lt;choice=Name&gt;</c> in the text, keyed by name (see <see cref="BuildInlineLinks"/>).</summary>
            public List<InlineLink>                 InlineLinks      { get; set; } = new();
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
            (List<StoryDeclaredVariable> incoming, List<Dictionary<Guid, string>> valueMaps, int total) = SectionsData(project, logic);

            List<Section> sections = valueMaps
                                    .Select(values => BuildSection(project, localization, logic, incoming, values))
                                    .ToList();

            // When the text references choices inline (<choice=Name>), those links replace the standalone continue lines.
            bool hasInline = sections.Exists(s => s.Rendered.InlineChoices.Count > 0);

            return new Result
            {
                Sections          = sections,
                Continue          = hasInline ? new List<ContinueLine>() : BuildContinueLines(project, localization, logic),
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
            List<InlineLink>                 inline   = BuildInlineLinks(project, localization, rendered);
            string                           label    = ComboLabel(incoming, values);
            string                           key      = SectionToken(logic, incoming, values);

            if (!logic.GamebookInstructions)
                return new Section { Label = label, Key = key, Rendered = rendered, InlineLinks = inline };

            return new Section
            {
                Label            = label,
                Key              = key,
                Rendered         = rendered,
                InlineLinks      = inline,
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
                        string section = SectionToken(next.Logic, new List<StoryDeclaredVariable>(), new Dictionary<Guid, string>());
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
