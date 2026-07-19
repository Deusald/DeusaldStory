using System;
using System.Collections.Generic;
using System.Linq;
using DeusaldLocalizerCommon;
using DeusaldStoryCommon;

namespace DeusaldStoryWeb
{
    /// <summary>
    /// Which half of the editor is active. In a container the pair is Edit / <see cref="Play"/>; inside a logic
    /// node it is Edit / <see cref="Preview"/> (only one of Play/Preview is ever offered at a time).
    /// </summary>
    public enum EditorMode
    {
        Edit,
        Play,
        Preview
    }

    /// <summary>Which kind of graph the editor is currently showing — a container's graph or a logic node's inner graph.</summary>
    public enum EditorScope
    {
        Container,
        Logic
    }

    // Ports are untyped — every wire carries plain flow. Variables are global, so nothing travels down a wire and
    // there is no compatibility rule to enforce; the node's own colour carries the kind distinction instead.

    /// <summary>
    /// One step in the editor's breadcrumb — the path the user has drilled into (containers, and finally a logic
    /// node's inner graph), e.g. <c>Base / Chapter 1 / Arrival</c>. <see cref="Scope"/> says whether this step
    /// shows a container's graph or a logic node's inner graph.
    /// </summary>
    public sealed record EditorCrumb(Guid Id, string Name, EditorScope Scope = EditorScope.Container);

    /// <summary>
    /// A request from the Gamebook preview to follow a clicked "go to section" line: switch the preview to
    /// <see cref="LogicId"/> and scroll to the section whose <see cref="StoryGamebookPreview.Section.Key"/> is
    /// <see cref="SectionKey"/>.
    /// </summary>
    public sealed record GamebookNavRequest(Guid LogicId, string SectionKey);

    /// <summary>Visual/semantic kind of a story node, mapped to a colour + label in the graph.</summary>
    public enum StoryNodeKind
    {
        Start,      // the root container's single entry — where the story begins (green)
        End,        // the root container's single exit — where the story ends (red)
        Entry,      // a non-root container's entry point, rendered as a node inside it (green)
        Exit,       // a non-root container's exit point, rendered as a node inside it (red)
        Logic,      // a logic node — a stop point that generates content and runs calculations (yellow)
        Container,  // a child container node (blue)
        PortalIn,   // a portal's entry node — flow arriving here teleports to the paired portal out (orange)
        PortalOut,  // a portal's exit node — flow re-emerges here and continues on (orange)
        LogicEntry, // inside a logic node: the single Entry node — title/subtitle/icon config + flow output (green)
        LogicExit,  // inside a logic node: the single Exit node terminating the flow spine (red)
        Text,       // inside a logic node: on the flow spine, renders a text block then continues (accent)
        ConstantVariable,      // inside a logic node: on the flow spine, publishes a named constant into the dictionary (teal)
        RandomizedInstruction, // inside a logic node: renders a random choice — App drawn value / Gamebook D12 band table (purple)
        SplitForApp,           // inside a logic node: on the flow spine, breaks the App render into a new "continue" page (orange)
        SetVariable,           // inside a logic node: on the flow spine, sets a global variable's value (pink)
        ConditionFlow,         // inside a logic node: on the flow spine, injects an optional block of flow gated by a condition (amber)
        EndConditionFlow,      // inside a logic node: the paired terminator that closes a Condition node's injected block (amber)
        Comment                // in a container's graph or a logic node's inner graph: a free-text author note, no ports (dim)
    }

    /// <summary>A point on the graph canvas in world (un-panned, un-scaled) coordinates.</summary>
    public readonly record struct CanvasPoint(double X, double Y);

    /// <summary>The graph canvas viewport — its pan offset (screen px) and zoom scale — so it can be restored after leaving/returning to the graph (e.g. an edit → preview → edit round-trip).</summary>
    public readonly record struct GraphViewport(double PanX, double PanY, double Scale);

    /// <summary>
    /// One selectable entry in the right-click node palette. <see cref="Kind"/> tells the editor which kind of
    /// node to create; the rest is presentation. The available set is context-dependent (see the palette).
    /// </summary>
    public sealed record NodePaletteItem(StoryNodeKind Kind, string Icon, string Label, string Description);

    /// <summary>An input or output connection port drawn on a node card.</summary>
    public sealed class EdPort
    {
        public Guid   Id   { get; init; }
        public string Name { get; init; } = "";
    }

    /// <summary>A node as drawn on the graph canvas, projected from the persisted story model.</summary>
    public sealed class EdNode
    {
        public Guid          Id        { get; init; }
        public StoryNodeKind Kind      { get; set; }
        public string        Title     { get; set; } = "";
        public string        Subtitle  { get; set; } = "";
        public double        X         { get; set; }
        public double        Y         { get; set; }
        public bool          Deletable { get; init; }
        public bool          Editable  { get; init; } = true;

        /// <summary>Optional inline thumbnail (base64 PNG) drawn in the card body — e.g. an Icon node's picked icon.</summary>
        public string? Thumb { get; set; }

        /// <summary>For a portal node (container or logic), the id of the node on the <b>opposite</b> side of the pair —
        /// an "in" points at an "out" and vice versa — so the context menu can jump the view across the pair. Null on
        /// non-portal nodes.</summary>
        public Guid? PortalCrossTarget { get; set; }

        /// <summary>For a portal side that has siblings (a many-in container portal's ins, or a many-out logic portal's
        /// outs), the id of the <b>next</b> sibling of the same role, cycling round — so the menu can offer "Go to next
        /// in/out". Null when this side has only one member.</summary>
        public Guid? PortalNextTarget { get; set; }

        public List<EdPort> Inputs  { get; } = new();
        public List<EdPort> Outputs { get; } = new();
    }

    /// <summary>
    /// A directed connection drawn on the canvas: from an output port (<see cref="FromPoint"/>) to an input
    /// port (<see cref="ToPoint"/>). Ports are identified by their connection-point id; the graph resolves each
    /// to its owning node and exact anchor position.
    /// </summary>
    public sealed class EdEdge
    {
        public Guid Id        { get; init; }
        public Guid FromPoint { get; init; }
        public Guid ToPoint   { get; init; }
    }

    /// <summary>A user request to wire an output port to an input port, raised by the graph on drop.</summary>
    public readonly record struct EdConnectRequest(Guid FromPoint, Guid ToPoint);

    /// <summary>
    /// A user request to move a node (a logic or container node) into a container, raised by the graph when the
    /// node is dropped onto that container's card. <see cref="TargetContainerId"/> is the drop-target container.
    /// </summary>
    public readonly record struct EdNodeReparent(Guid NodeId, Guid TargetContainerId);

    /// <summary>Projects a persisted story container into the editor's canvas view models.</summary>
    public static class EditorProjection
    {
        /// <summary>
        /// Builds the graph nodes for <paramref name="container"/>: its own entry/exit points (Start/End in the
        /// root, Entry/Exit otherwise) as non-deletable nodes, plus its child logic and container nodes.
        /// </summary>
        public static List<EdNode> BuildNodes(StoryProject project, StoryContainerNode container, bool isRoot)
        {
            List<EdNode> nodes = new();

            foreach (StoryConnectionPoint ep in container.EntryPoints)
            {
                EdNode node = new()
                {
                    Id        = ep.Id,
                    Kind      = isRoot ? StoryNodeKind.Start : StoryNodeKind.Entry,
                    Title     = ep.Name,
                    X         = ep.X,
                    Y         = ep.Y,
                    Deletable = false,
                    Editable  = false
                };
                node.Outputs.Add(new EdPort { Id = ep.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow) });
                nodes.Add(node);
            }

            foreach (StoryConnectionPoint xp in container.ExitPoints)
            {
                EdNode node = new()
                {
                    Id        = xp.Id,
                    Kind      = isRoot ? StoryNodeKind.End : StoryNodeKind.Exit,
                    Title     = xp.Name,
                    X         = xp.X,
                    Y         = xp.Y,
                    Deletable = false,
                    Editable  = false
                };
                node.Inputs.Add(new EdPort { Id = xp.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow) });
                nodes.Add(node);
            }

            foreach (Guid childId in container.Logic)
            {
                if (!project.LogicNodes.TryGetValue(childId, out StoryLogicNode? logic)) continue;

                EdNode node = new()
                {
                    Id        = logic.Id,
                    Kind      = StoryNodeKind.Logic,
                    Title     = logic.Name,
                    Subtitle  = logic.Description,
                    X         = logic.X,
                    Y         = logic.Y,
                    Deletable = true
                };
                // Accept-variables makes the single entry a VFlow input (flow that also carries the upstream variables).
                // Label the port after its type so the author can tell a plain-flow entry from a variable-carrying one.
                node.Inputs.Add(new EdPort { Id = logic.EntryPoint.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow) });

                if (logic.ExitMode == StoryLogicExitMode.SinglePath)
                    node.Outputs.Add(new EdPort { Id = logic.SingleOut.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.continueLabel) });
                else
                    node.Outputs.AddRange(logic.Choices.Select(c =>
                        new EdPort { Id = c.OuterFlowOut.Id, Name = string.IsNullOrWhiteSpace(c.Name) ? UiLang.T(Localization.Editor.Nodes.Ports.choice) : c.Name }));
                nodes.Add(node);
            }

            foreach (Guid portalId in container.Portals)
            {
                if (!project.PortalNodes.TryGetValue(portalId, out StoryPortalNode? portal)) continue;

                for (int x = 0; x < portal.InPoints.Count; ++x)
                {
                    StoryConnectionPoint ip = portal.InPoints[x];
                    EdNode inNode = new()
                    {
                        Id                = ip.Id,
                        Kind              = StoryNodeKind.PortalIn,
                        Title             = portal.Name,
                        Subtitle          = portal.Description,
                        X                 = ip.X,
                        Y                 = ip.Y,
                        Deletable         = true,
                        // Many-in / one-out: the cross-jump lands on the single out; the next-jump cycles the other ins.
                        PortalCrossTarget = portal.OutPoint.Id,
                        PortalNextTarget  = portal.InPoints.Count > 1 ? portal.InPoints[(x + 1) % portal.InPoints.Count].Id : null
                    };
                    inNode.Inputs.Add(new EdPort { Id = ip.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow) });
                    nodes.Add(inNode);
                }

                EdNode outNode = new()
                {
                    Id                = portal.OutPoint.Id,
                    Kind              = StoryNodeKind.PortalOut,
                    Title             = portal.Name,
                    Subtitle          = portal.Description,
                    X                 = portal.OutPoint.X,
                    Y                 = portal.OutPoint.Y,
                    Deletable         = true,
                    // The single out jumps back to the first in (no next-jump — there is only one out).
                    PortalCrossTarget = portal.InPoints.Count > 0 ? portal.InPoints[0].Id : null
                };
                outNode.Outputs.Add(new EdPort { Id = portal.OutPoint.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow) });
                nodes.Add(outNode);
            }

            foreach (Guid childId in container.Containers)
            {
                if (!project.ContainerNodes.TryGetValue(childId, out StoryContainerNode? child)) continue;

                EdNode node = new()
                {
                    Id        = child.Id,
                    Kind      = StoryNodeKind.Container,
                    Title     = child.Name,
                    Subtitle  = child.Description,
                    X         = child.X,
                    Y         = child.Y,
                    Deletable = true
                };
                node.Inputs.AddRange(child.EntryPoints.Select(p => new EdPort { Id = p.Id, Name = p.Name }));
                node.Outputs.AddRange(child.ExitPoints.Select(p => new EdPort { Id = p.Id, Name = p.Name }));
                nodes.Add(node);
            }

            foreach (StoryCommentNode comment in container.Comments)
                nodes.Add(BuildCommentNode(comment));

            return nodes;
        }

        /// <summary>Projects a comment note into a portless canvas node whose <see cref="EdNode.Title"/> carries its text.</summary>
        private static EdNode BuildCommentNode(StoryCommentNode comment) => new()
        {
            Id        = comment.Id,
            Kind      = StoryNodeKind.Comment,
            Title     = comment.Text,
            X         = comment.X,
            Y         = comment.Y,
            Deletable = true,
            Editable  = true
        };

        /// <summary>Projects a container's persisted connections into canvas edges.</summary>
        public static List<EdEdge> BuildEdges(StoryContainerNode container) =>
            container.Connections
                     .Select(c => new EdEdge { Id = c.Id, FromPoint = c.FromPoint, ToPoint = c.ToPoint })
                     .ToList();

        // ── Logic node inner graph ─────────────────────────────────────────────

        // Fallback positions for a logic node's Entry/Exit points that were never placed (legacy nodes, or the
        // rare (0,0)). New nodes get real coordinates at creation; these keep an un-placed graph from stacking.
        private const double _LOGIC_ENTRY_X = 60;
        private const double _LOGIC_ENTRY_Y = 200;
        private const double _LOGIC_EXIT_X  = 660;
        private const double _LOGIC_EXIT_Y0 = 120;
        private const double _LOGIC_EXIT_DY = 90;
        private const double _PREV_EXIT_Y   = 360;

        /// <summary>
        /// Builds the graph nodes for a logic node's inner content graph: its single Entry node (the reused
        /// <see cref="StoryLogicNode.EntryPoint"/>), the single Exit node terminating the spine, and every content
        /// node on that spine. Every node has exactly one flow in and one flow out, so the projection is uniform —
        /// what each node <i>does</i> is configured in the inspector, not wired.
        /// </summary>
        public static List<EdNode> BuildLogicNodes(StoryProject project, StoryLogicNode logic, LocProject? localization)
        {
            List<EdNode> nodes = new();

            // ── Entry node (green) — flow starts here. Title/subtitle/icon are configured on it, not wired in. ──
            (double ex, double ey) = PointPos(logic.EntryPoint, _LOGIC_ENTRY_X, _LOGIC_ENTRY_Y);
            EdNode entry = new()
            {
                Id        = logic.EntryPoint.Id,
                Kind      = StoryNodeKind.LogicEntry,
                Title     = UiLang.T(Localization.Editor.Nodes.Titles.entry),
                Subtitle  = EntrySummary(logic, localization),
                X         = ex,
                Y         = ey,
                Deletable = false,
                Editable  = true,
                Thumb     = EntryThumb(project, logic)
            };
            entry.Outputs.Add(new EdPort { Id = logic.EntryPoint.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow) });
            nodes.Add(entry);

            // ── Exit node (red) — the single terminator. Choices are configured on it, not wired into it. ──
            (double xx, double xy) = PointPos(logic.ExitFlowIn, _LOGIC_EXIT_X, _LOGIC_EXIT_Y0);
            EdNode exit = new()
            {
                Id        = logic.ExitFlowIn.Id,
                Kind      = StoryNodeKind.LogicExit,
                Title     = UiLang.T(Localization.Editor.Nodes.Titles.exit),
                Subtitle  = ExitSummary(project, logic),
                X         = xx,
                Y         = xy,
                Deletable = false,
                Editable  = true
            };
            exit.Inputs.Add(new EdPort { Id = logic.ExitFlowIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow) });
            nodes.Add(exit);

            // ── Text nodes ──
            foreach (StoryTextNode text in logic.TextNodes)
            {
                EdNode node = new()
                {
                    Id        = text.Id,
                    Kind      = StoryNodeKind.Text,
                    Title     = StoryStyle.NodeLabel(StoryNodeKind.Text),
                    Subtitle  = TextNodeSummary(text) ?? TextPreview(localization, text.Text),
                    X         = text.X,
                    Y         = text.Y,
                    Deletable = true,
                    Editable  = true
                };
                AddFlowPorts(node, text.FlowIn.Id, text.FlowOut.Id);
                nodes.Add(node);
            }

            // ── Split for App ──
            foreach (StorySplitForAppNode split in logic.SplitForAppNodes)
            {
                EdNode node = new()
                {
                    Id        = split.Id,
                    Kind      = StoryNodeKind.SplitForApp,
                    Title     = StoryStyle.NodeLabel(StoryNodeKind.SplitForApp),
                    X         = split.X,
                    Y         = split.Y,
                    Deletable = true,
                    Editable  = false
                };
                AddFlowPorts(node, split.FlowIn.Id, split.FlowOut.Id);
                nodes.Add(node);
            }

            // ── Constant Variable ──
            foreach (StoryConstantVariableNode constant in logic.ConstantVariableNodes)
            {
                EdNode node = new()
                {
                    Id        = constant.Id,
                    Kind      = StoryNodeKind.ConstantVariable,
                    Title     = string.IsNullOrWhiteSpace(constant.Name) ? StoryStyle.NodeLabel(StoryNodeKind.ConstantVariable) : constant.Name,
                    Subtitle  = constant.Value,
                    X         = constant.X,
                    Y         = constant.Y,
                    Deletable = true,
                    Editable  = true
                };
                AddFlowPorts(node, constant.FlowIn.Id, constant.FlowOut.Id);
                nodes.Add(node);
            }

            // ── Set Variable ──
            foreach (StorySetVariableNode set in logic.SetVariableNodes)
            {
                StoryVariable? target = StoryLogicFlow.SetTarget(project, set);
                EdNode node = new()
                {
                    Id        = set.Id,
                    Kind      = StoryNodeKind.SetVariable,
                    Title     = StoryStyle.NodeLabel(StoryNodeKind.SetVariable),
                    Subtitle  = target?.Name ?? UiLang.T(Localization.Common.Placeholders.unnamed),
                    X         = set.X,
                    Y         = set.Y,
                    Deletable = true,
                    Editable  = true
                };
                AddFlowPorts(node, set.FlowIn.Id, set.FlowOut.Id);
                nodes.Add(node);
            }

            // ── Randomized Instruction ──
            foreach (StoryRandomizedInstructionNode random in logic.RandomizedInstructionNodes)
            {
                EdNode node = new()
                {
                    Id        = random.Id,
                    Kind      = StoryNodeKind.RandomizedInstruction,
                    Title     = StoryStyle.NodeLabel(StoryNodeKind.RandomizedInstruction),
                    Subtitle  = string.IsNullOrWhiteSpace(random.ResultToken) ? "" : random.ResultToken,
                    X         = random.X,
                    Y         = random.Y,
                    Deletable = true,
                    Editable  = true
                };
                AddFlowPorts(node, random.FlowIn.Id, random.FlowOut.Id);
                nodes.Add(node);
            }

            // ── Condition pairs — the gate card and its paired terminator. ──
            foreach (StoryConditionFlowNode cf in logic.ConditionFlowNodes)
            {
                EdNode condition = new()
                {
                    Id        = cf.Id,
                    Kind      = StoryNodeKind.ConditionFlow,
                    Title     = string.IsNullOrWhiteSpace(cf.Name) ? StoryStyle.NodeLabel(StoryNodeKind.ConditionFlow) : cf.Name,
                    Subtitle  = cf.Negate ? UiLang.T(Localization.Editor.Nodes.Titles.conditionNegated) : "",
                    X         = cf.X,
                    Y         = cf.Y,
                    Deletable = true,
                    Editable  = true
                };
                condition.Inputs.Add(new EdPort { Id = cf.FlowIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow) });
                condition.Outputs.Add(new EdPort { Id = cf.ContinueOut.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.continueLabel) });
                condition.Outputs.Add(new EdPort { Id = cf.ConditionTrueOut.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.conditionTrue) });
                nodes.Add(condition);

                EdNode end = new()
                {
                    Id        = cf.EndId,
                    Kind      = StoryNodeKind.EndConditionFlow,
                    Title     = StoryStyle.NodeLabel(StoryNodeKind.EndConditionFlow),
                    X         = cf.EndX,
                    Y         = cf.EndY,
                    Deletable = true,
                    Editable  = false
                };
                end.Inputs.Add(new EdPort { Id = cf.EndFlowIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow) });
                nodes.Add(end);
            }

            foreach (StoryCommentNode comment in logic.CommentNodes)
                nodes.Add(BuildCommentNode(comment));

            return nodes;
        }

        /// <summary>Adds the one-in / one-out flow ports every spine node carries.</summary>
        private static void AddFlowPorts(EdNode node, Guid flowIn, Guid flowOut)
        {
            node.Inputs.Add(new EdPort { Id = flowIn, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow) });
            node.Outputs.Add(new EdPort { Id = flowOut, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow) });
        }

        /// <summary>A one-line preview of an authored text — its main-language localized text, trimmed to fit a card.</summary>
        private static string TextPreview(LocProject? localization, StoryTextConfig config)
        {
            if (config.IsEmpty) return "";
            string text = StoryLogicRenderer.LocalizedText(localization, config.KeyId).Replace('\n', ' ').Trim();
            return text.Length <= 60 ? text : text.Substring(0, 57) + "…";
        }

        /// <summary>The Entry card's subtitle — its title text, so the card reads as the screen it produces.</summary>
        private static string EntrySummary(StoryLogicNode logic, LocProject? localization) =>
            TextPreview(localization, logic.EntryTitle);

        /// <summary>The Entry card's thumbnail — whichever icon slot is filled (dark preferred, matching the editor's own theme).</summary>
        private static string? EntryThumb(StoryProject project, StoryLogicNode logic)
        {
            Guid id = logic.DarkIcon != Guid.Empty ? logic.DarkIcon : logic.LightIcon;
            if (id == Guid.Empty) return null;
            return project.Images.TryGetValue(id, out StoryImage? img) ? img.Data : null;
        }

        /// <summary>The Exit card's subtitle — how many continuations this node offers, and how they are decided.</summary>
        private static string ExitSummary(StoryProject project, StoryLogicNode logic)
        {
            if (logic.ExitMode == StoryLogicExitMode.SinglePath)
            {
                StoryChoiceSources.Combinations(project, logic, StoryGamebookPreview.MAX_SECTIONS, out int total);
                return logic.ChoiceDefinitions.Count == 0
                    ? ""
                    : UiLang.T(Localization.Editor.Nodes.Titles.exitChoiceCount, new Dictionary<string, object> { ["count"] = total });
            }

            return logic.Choices.Count == 0
                ? ""
                : UiLang.T(Localization.Editor.Nodes.Titles.exitChoiceCount, new Dictionary<string, object> { ["count"] = logic.Choices.Count });
        }

        /// <summary>A compact symbol for a condition operator (used in the condition editor).</summary>
        public static string ConditionSymbol(StoryConditionOperator op) => op switch
        {
            StoryConditionOperator.Equal          => "=",
            StoryConditionOperator.NotEqual       => "≠",
            StoryConditionOperator.LessThan       => "<",
            StoryConditionOperator.GreaterThan    => ">",
            StoryConditionOperator.LessOrEqual    => "≤",
            StoryConditionOperator.GreaterOrEqual => "≥",
            _                                     => "?"
        };

        /// <summary>
        /// Subtitle for a Text node — the non-Normal frame style and/or a medium restriction, joined with " · ".
        /// Null when the block renders in both mediums with the plain Normal frame (nothing worth flagging), so the
        /// caller can fall back to previewing the text itself.
        /// </summary>
        private static string? TextNodeSummary(StoryTextNode text)
        {
            string? medium = (text.RenderInApp, text.RenderInGamebook) switch
            {
                (true,  true)  => null,
                (true,  false) => UiLang.T(Localization.Editor.Nodes.Mediums.appOnly),
                (false, true)  => UiLang.T(Localization.Editor.Nodes.Mediums.gamebookOnly),
                (false, false) => UiLang.T(Localization.Editor.Nodes.Mediums.notRendered)
            };
            string? frame = text.FrameStyle == StoryTextFrameStyle.Normal ? null : StoryStyle.FrameLabel(text.FrameStyle);
            return (frame, medium) switch
            {
                (null, null) => null,
                (null, _)    => medium,
                (_,    null) => frame,
                _            => $"{frame} · {medium}"
            };
        }

        /// <summary>Projects a logic node's inner content connections into canvas edges.</summary>
        public static List<EdEdge> BuildLogicEdges(StoryLogicNode logic) =>
            logic.ContentConnections
                 .Select(c => new EdEdge { Id = c.Id, FromPoint = c.FromPoint, ToPoint = c.ToPoint })
                 .ToList();

        /// <summary>A connection point's stored position, falling back to (<paramref name="defX"/>, <paramref name="defY"/>) when unplaced.</summary>
        private static (double X, double Y) PointPos(StoryConnectionPoint p, double defX, double defY) =>
            p.X == 0 && p.Y == 0 ? (defX, defY) : (p.X, p.Y);

        /// <summary>The key's full path — its category chain (root → leaf) joined with '/' then the key name.</summary>
        private static string FullKeyName(LocLocalizationKey key, LocProject localization)
        {
            List<string> parts = new() { key.KeyName };
            Guid?        cur    = key.CategoryId == Guid.Empty ? null : key.CategoryId;
            int          guard  = 0;
            while (cur is Guid id && localization.Categories.Find(c => c.Id == id) is LocCategory cat && guard++ < 32)
            {
                parts.Insert(0, cat.Name);
                cur = cat.ParentCategoryId;
            }
            return string.Join("/", parts);
        }

        /// <summary>The main-language text of <paramref name="key"/> (empty when there is no source translation).</summary>
        private static string PreviewText(LocLocalizationKey key, LocProject? localization)
        {
            string? mainLang = localization?.Metadata.MainLanguageId;
            if (string.IsNullOrEmpty(mainLang)) return "";
            return key.Translations.Find(t => t.LanguageId == mainLang)?.Text ?? "";
        }
    }

    /// <summary>Maps the editor view-model kinds to their design-token colours and labels.</summary>
    public static class StoryStyle
    {
        public static string NodeLabel(StoryNodeKind k) => k switch
        {
            StoryNodeKind.Start     => UiLang.T(Localization.Editor.Nodes.Labels.start),
            StoryNodeKind.End       => UiLang.T(Localization.Editor.Nodes.Labels.end),
            StoryNodeKind.Entry     => UiLang.T(Localization.Editor.Nodes.Labels.entry),
            StoryNodeKind.Exit      => UiLang.T(Localization.Editor.Nodes.Labels.exit),
            StoryNodeKind.Logic     => UiLang.T(Localization.Editor.Nodes.Labels.logic),
            StoryNodeKind.Container => UiLang.T(Localization.Editor.Nodes.Labels.container),
            StoryNodeKind.PortalIn  => UiLang.T(Localization.Editor.Nodes.Labels.portalIn),
            StoryNodeKind.PortalOut => UiLang.T(Localization.Editor.Nodes.Labels.portalOut),
            StoryNodeKind.LogicEntry   => UiLang.T(Localization.Editor.Nodes.Labels.entry),
            StoryNodeKind.LogicExit    => UiLang.T(Localization.Editor.Nodes.Labels.exit),
            StoryNodeKind.ConstantVariable => UiLang.T(Localization.Editor.Nodes.Labels.constantVariable),
            StoryNodeKind.RandomizedInstruction => UiLang.T(Localization.Editor.Nodes.Labels.randomizedInstruction),
            StoryNodeKind.Text                    => UiLang.T(Localization.Editor.Nodes.Labels.flowText),
            StoryNodeKind.SplitForApp             => UiLang.T(Localization.Editor.Nodes.Labels.splitForApp),
            StoryNodeKind.SetVariable             => UiLang.T(Localization.Editor.Nodes.Labels.setVariable),
            StoryNodeKind.ConditionFlow           => UiLang.T(Localization.Editor.Nodes.Labels.condition),
            StoryNodeKind.EndConditionFlow        => UiLang.T(Localization.Editor.Nodes.Labels.endCondition),
            StoryNodeKind.Comment                 => UiLang.T(Localization.Editor.Nodes.Labels.comment),
            _                       => ""
        };

        /// <summary>
        /// True when a node's <see cref="EdNode.Title"/> carries no information beyond the kind label already shown in
        /// the header (e.g. a Text / Exit node whose title just repeats its type) — so callers can
        /// skip drawing the redundant title line. Compares case- and whitespace-insensitively.
        /// </summary>
        public static bool IsRedundantTitle(StoryNodeKind kind, string title)
        {
            static string Norm(string s) => new string(s.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
            return !string.IsNullOrWhiteSpace(title) && Norm(title) == Norm(NodeLabel(kind));
        }

        /// <summary>
        /// The Bootstrap icon shown in a node's header (in place of the old kind dot). Mirrors the icons the node
        /// palette uses for the kinds it offers; boundary/built-in kinds (Start/End/Entry/Exit/portals) that never
        /// appear in the palette get their own arrow glyphs.
        /// </summary>
        public static string NodeIcon(StoryNodeKind k) => k switch
        {
            StoryNodeKind.Start     => "bi-play-fill",
            StoryNodeKind.End       => "bi-stop-fill",
            StoryNodeKind.Entry     => "bi-box-arrow-in-right",
            StoryNodeKind.Exit      => "bi-box-arrow-right",
            StoryNodeKind.Logic     => "bi-diamond-half",
            StoryNodeKind.Container => "bi-collection",
            StoryNodeKind.PortalIn  => "bi-box-arrow-in-right",
            StoryNodeKind.PortalOut => "bi-box-arrow-right",
            StoryNodeKind.LogicEntry   => "bi-box-arrow-in-right",
            StoryNodeKind.LogicExit    => "bi-box-arrow-right",
            StoryNodeKind.Text                  => "bi-text-paragraph",
            StoryNodeKind.ConstantVariable      => "bi-braces",
            StoryNodeKind.RandomizedInstruction => "bi-dice-5",
            StoryNodeKind.SplitForApp           => "bi-scissors",
            StoryNodeKind.SetVariable           => "bi-pencil-square",
            StoryNodeKind.ConditionFlow         => "bi-signpost-split",
            StoryNodeKind.EndConditionFlow        => "bi-sign-merge-left",
            StoryNodeKind.Comment                 => "bi-chat-left-text",
            _                       => "bi-circle"
        };

        /// <summary>
        /// The colour of every port dot and wire. All ports now carry plain flow — nothing travels down a wire since
        /// variables became global — so a single neutral tint keeps the graph readable and lets the <b>node</b> colour
        /// (see <see cref="NodeColor"/>) carry the kind distinction on its own.
        /// </summary>
        public const string WireColor = "var(--text-muted)";

        /// <summary>
        /// A node's accent colour, distinct per kind so a graph reads at a glance. Container-graph kinds and a logic
        /// node's inner-graph kinds never share a canvas, so the two sets reuse the same hues independently.
        /// </summary>
        public static string NodeColor(StoryNodeKind k) => k switch
        {
            // ── Container graph ──
            StoryNodeKind.Start     => "var(--success)",
            StoryNodeKind.Entry     => "var(--success)",
            StoryNodeKind.End       => "var(--danger)",
            StoryNodeKind.Exit      => "var(--danger)",
            StoryNodeKind.Logic     => "var(--warning)",
            StoryNodeKind.Container => "var(--info)",
            StoryNodeKind.PortalIn  => "var(--orange)",
            StoryNodeKind.PortalOut => "var(--orange)",

            // ── Logic node inner graph ──
            StoryNodeKind.LogicEntry            => "var(--success)",
            StoryNodeKind.LogicExit             => "var(--danger)",
            StoryNodeKind.Text                  => "var(--accent)",
            StoryNodeKind.SetVariable           => "var(--pink)",
            StoryNodeKind.ConstantVariable      => "var(--code-func)",
            StoryNodeKind.RandomizedInstruction => "var(--purple)",
            StoryNodeKind.SplitForApp           => "var(--orange)",
            StoryNodeKind.ConditionFlow         => "var(--warning)",
            StoryNodeKind.EndConditionFlow      => "var(--warning)",

            StoryNodeKind.Comment => "var(--text-dim)",
            _                     => "var(--text-dim)"
        };

        /// <summary>The CSS-modifier suffix for a text-block frame style ("success", "danger", …); empty for <see cref="StoryTextFrameStyle.Normal"/>.</summary>
        public static string FrameSuffix(StoryTextFrameStyle s) => s switch
        {
            StoryTextFrameStyle.Info    => "info",
            StoryTextFrameStyle.Success => "success",
            StoryTextFrameStyle.Warning => "warning",
            StoryTextFrameStyle.Danger  => "danger",
            _                           => ""
        };

        /// <summary>The human label for a text-block frame style, shown in the FlowText options dropdown and on the canvas node.</summary>
        public static string FrameLabel(StoryTextFrameStyle s) => s switch
        {
            StoryTextFrameStyle.Info    => UiLang.T(Localization.Editor.Nodes.Frames.info),
            StoryTextFrameStyle.Success => UiLang.T(Localization.Editor.Nodes.Frames.success),
            StoryTextFrameStyle.Warning => UiLang.T(Localization.Editor.Nodes.Frames.warning),
            StoryTextFrameStyle.Danger  => UiLang.T(Localization.Editor.Nodes.Frames.danger),
            _                           => UiLang.T(Localization.Editor.Nodes.Frames.normal)
        };

        /// <summary>The Bootstrap-icon class shown alongside a non-Normal frame style; empty for <see cref="StoryTextFrameStyle.Normal"/>.</summary>
        public static string FrameIcon(StoryTextFrameStyle s) => s switch
        {
            StoryTextFrameStyle.Info    => "bi-info-circle-fill",
            StoryTextFrameStyle.Success => "bi-check-circle-fill",
            StoryTextFrameStyle.Warning => "bi-exclamation-triangle-fill",
            StoryTextFrameStyle.Danger  => "bi-exclamation-octagon-fill",
            _                           => ""
        };
    }
}
