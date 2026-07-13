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

    /// <summary>
    /// What a connection port carries, so wiring only joins compatible ports. Ordinary story flow is
    /// <see cref="Flow"/> (all container/logic-boundary ports, and the FlowText spine); resolved localization /
    /// SmartFormat text uses <see cref="Text"/>; icons use <see cref="Icon"/>; variable values use
    /// <see cref="Variable"/>.
    /// </summary>
    public enum PortType
    {
        Flow,
        Text,
        Icon,
        Variable
    }

    /// <summary>
    /// One step in the editor's breadcrumb — the path the user has drilled into (containers, and finally a logic
    /// node's inner graph), e.g. <c>Base / Chapter 1 / Arrival</c>. <see cref="Scope"/> says whether this step
    /// shows a container's graph or a logic node's inner graph.
    /// </summary>
    public sealed record EditorCrumb(Guid Id, string Name, EditorScope Scope = EditorScope.Container);

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
        LogicEntry, // inside a logic node: the single Entry node (Title/Icon inputs + flow output) (green)
        LogicExit,  // inside a logic node: one Exit node per exit branch (flow input) (red)
        Localization, // inside a logic node: picks a localization key, emits its text (accent)
        Icon,       // inside a logic node: picks a project icon, feeds an Icon input (orange)
        LightDarkSwitch, // inside a logic node: picks between two icons by render theme (info)
        SmartFormat,     // inside a logic node: formats a text with connected variable values (purple)
        ExternalVariable, // inside a logic node: picks a story variable, feeds a SmartFormat variables input (teal)
        FlowText         // inside a logic node: on the flow spine, renders a text block then continues flow (amber)
    }

    /// <summary>A point on the graph canvas in world (un-panned, un-scaled) coordinates.</summary>
    public readonly record struct CanvasPoint(double X, double Y);

    /// <summary>
    /// One selectable entry in the right-click node palette. <see cref="Kind"/> tells the editor which kind of
    /// node to create; the rest is presentation. The available set is context-dependent (see the palette).
    /// </summary>
    public sealed record NodePaletteItem(StoryNodeKind Kind, string Icon, string Label, string Description);

    /// <summary>An input or output connection port drawn on a node card.</summary>
    public sealed class EdPort
    {
        public Guid     Id   { get; init; }
        public string   Name { get; init; } = "";
        public PortType Type { get; init; } = PortType.Flow;
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
                    Deletable = false
                };
                node.Outputs.Add(new EdPort { Id = ep.Id, Name = ep.Name });
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
                    Deletable = false
                };
                node.Inputs.Add(new EdPort { Id = xp.Id, Name = xp.Name });
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
                node.Inputs.Add(new EdPort { Id = logic.EntryPoint.Id, Name = logic.EntryPoint.Name });
                node.Outputs.AddRange(logic.ExitPoints.Select(p => new EdPort { Id = p.Id, Name = p.Name }));
                nodes.Add(node);
            }

            foreach (Guid portalId in container.Portals)
            {
                if (!project.PortalNodes.TryGetValue(portalId, out StoryPortalNode? portal)) continue;

                foreach (StoryConnectionPoint ip in portal.InPoints)
                {
                    EdNode inNode = new()
                    {
                        Id        = ip.Id,
                        Kind      = StoryNodeKind.PortalIn,
                        Title     = portal.Name,
                        Subtitle  = portal.Description,
                        X         = ip.X,
                        Y         = ip.Y,
                        Deletable = true
                    };
                    inNode.Inputs.Add(new EdPort { Id = ip.Id, Name = ip.Name });
                    nodes.Add(inNode);
                }

                EdNode outNode = new()
                {
                    Id        = portal.OutPoint.Id,
                    Kind      = StoryNodeKind.PortalOut,
                    Title     = portal.Name,
                    Subtitle  = portal.Description,
                    X         = portal.OutPoint.X,
                    Y         = portal.OutPoint.Y,
                    Deletable = true
                };
                outNode.Outputs.Add(new EdPort { Id = portal.OutPoint.Id, Name = portal.OutPoint.Name });
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

            return nodes;
        }

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

        /// <summary>
        /// Builds the graph nodes for a logic node's inner content graph: its single Entry node (the reused
        /// <see cref="StoryLogicNode.EntryPoint"/>, with the extra Title/Icon config inputs), one Exit node per
        /// <see cref="StoryLogicNode.ExitPoints"/> entry, and every Localization/Icon content node. Names for
        /// content nodes are resolved from <paramref name="localization"/> and the project's image library.
        /// </summary>
        public static List<EdNode> BuildLogicNodes(StoryProject project, StoryLogicNode logic, LocProject? localization)
        {
            List<EdNode> nodes = new();

            // ── Entry node (green) — flow starts here; Title/Icon feed its content. ──
            (double ex, double ey) = PointPos(logic.EntryPoint, _LOGIC_ENTRY_X, _LOGIC_ENTRY_Y);
            EdNode entry = new()
            {
                Id        = logic.EntryPoint.Id,
                Kind      = StoryNodeKind.LogicEntry,
                Title     = "Entry",
                X         = ex,
                Y         = ey,
                Deletable = false
            };
            entry.Inputs.Add(new EdPort { Id = logic.TitleIn.Id, Name = "Title", Type = PortType.Text });
            entry.Inputs.Add(new EdPort { Id = logic.IconIn.Id,  Name = "Icon",  Type = PortType.Icon });
            entry.Outputs.Add(new EdPort { Id = logic.EntryPoint.Id, Name = "Out", Type = PortType.Flow });
            nodes.Add(entry);

            // ── Exit nodes (red) — one per branch. ──
            for (int i = 0; i < logic.ExitPoints.Count; ++i)
            {
                StoryConnectionPoint xp = logic.ExitPoints[i];
                (double xx, double xy) = PointPos(xp, _LOGIC_EXIT_X, _LOGIC_EXIT_Y0 + i * _LOGIC_EXIT_DY);
                EdNode exit = new()
                {
                    Id        = xp.Id,
                    Kind      = StoryNodeKind.LogicExit,
                    Title     = xp.Name,
                    X         = xx,
                    Y         = xy,
                    Deletable = false
                };
                exit.Inputs.Add(new EdPort { Id = xp.Id, Type = PortType.Flow });
                nodes.Add(exit);
            }

            // ── Localization nodes (accent) — show the full category/key path + preview text. ──
            foreach (StoryLocalizationNode loc in logic.LocalizationNodes)
            {
                LocLocalizationKey? key = localization?.Keys.Find(k => k.Id == loc.SelectedKeyId);
                EdNode node = new()
                {
                    Id        = loc.Id,
                    Kind      = StoryNodeKind.Localization,
                    Title     = key is not null ? FullKeyName(key, localization!) : "(no key)",
                    Subtitle  = key is not null ? PreviewText(key, localization) : "",
                    X         = loc.X,
                    Y         = loc.Y,
                    Deletable = true
                };
                node.Outputs.Add(new EdPort { Id = loc.OutPoint.Id, Name = "Text", Type = PortType.Text });
                nodes.Add(node);
            }

            // ── Icon nodes (orange) — show the picked icon as a thumbnail. ──
            foreach (StoryIconNode ico in logic.IconNodes)
            {
                bool found = project.Images.TryGetValue(ico.SelectedImageId, out StoryImage? image);
                EdNode node = new()
                {
                    Id        = ico.Id,
                    Kind      = StoryNodeKind.Icon,
                    Title     = found ? image!.Name : "(no icon)",
                    Thumb     = found ? image!.Data : null,
                    X         = ico.X,
                    Y         = ico.Y,
                    Deletable = true
                };
                node.Outputs.Add(new EdPort { Id = ico.OutPoint.Id, Name = "Icon", Type = PortType.Icon });
                nodes.Add(node);
            }

            // ── Light/Dark switch nodes (info) — two icon inputs, one icon output; no config to edit. ──
            foreach (StoryLightDarkSwitchNode lds in logic.LightDarkSwitchNodes)
            {
                EdNode node = new()
                {
                    Id        = lds.Id,
                    Kind      = StoryNodeKind.LightDarkSwitch,
                    Title     = "Light / Dark",
                    X         = lds.X,
                    Y         = lds.Y,
                    Deletable = true,
                    Editable  = false
                };
                node.Inputs.Add(new EdPort { Id = lds.DarkIn.Id,  Name = "Dark",  Type = PortType.Icon });
                node.Inputs.Add(new EdPort { Id = lds.LightIn.Id, Name = "Light", Type = PortType.Icon });
                node.Outputs.Add(new EdPort { Id = lds.OutPoint.Id, Name = "Icon", Type = PortType.Icon });
                nodes.Add(node);
            }

            // ── SmartFormat nodes (purple) — a text input, a many-to-one variables input, one formatted output. ──
            foreach (StorySmartFormatNode sf in logic.SmartFormatNodes)
            {
                EdNode node = new()
                {
                    Id        = sf.Id,
                    Kind      = StoryNodeKind.SmartFormat,
                    Title     = "SmartFormat",
                    X         = sf.X,
                    Y         = sf.Y,
                    Deletable = true,
                    Editable  = false
                };
                node.Inputs.Add(new EdPort { Id = sf.LocalizationIn.Id, Name = "Text",      Type = PortType.Text });
                node.Inputs.Add(new EdPort { Id = sf.VariablesIn.Id,    Name = "Variables", Type = PortType.Variable });
                node.Outputs.Add(new EdPort { Id = sf.OutPoint.Id, Name = "Text", Type = PortType.Text });
                nodes.Add(node);
            }

            // ── External Variable nodes (teal) — reference a story variable, feed a SmartFormat variables input. ──
            foreach (StoryExternalVariableNode ev in logic.ExternalVariableNodes)
            {
                bool found = project.Variables.TryGetValue(ev.SelectedVariableId, out StoryVariable? variable);
                EdNode node = new()
                {
                    Id        = ev.Id,
                    Kind      = StoryNodeKind.ExternalVariable,
                    Title     = found ? variable!.Name : "(no variable)",
                    Subtitle  = found ? variable!.Description : "",
                    X         = ev.X,
                    Y         = ev.Y,
                    Deletable = true
                };
                node.Outputs.Add(new EdPort { Id = ev.OutPoint.Id, Name = "Value", Type = PortType.Variable });
                nodes.Add(node);
            }

            // ── FlowText nodes (amber) — sit on the flow spine, render a text block, continue flow. ──
            foreach (StoryFlowTextNode ft in logic.FlowTextNodes)
            {
                EdNode node = new()
                {
                    Id        = ft.Id,
                    Kind      = StoryNodeKind.FlowText,
                    Title     = "FlowText",
                    X         = ft.X,
                    Y         = ft.Y,
                    Deletable = true,
                    Editable  = false
                };
                node.Inputs.Add(new EdPort { Id = ft.FlowIn.Id, Name = "Flow", Type = PortType.Flow });
                node.Inputs.Add(new EdPort { Id = ft.TextIn.Id, Name = "Text", Type = PortType.Text });
                node.Outputs.Add(new EdPort { Id = ft.FlowOut.Id, Name = "Flow", Type = PortType.Flow });
                nodes.Add(node);
            }

            return nodes;
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
            StoryNodeKind.Start     => "START",
            StoryNodeKind.End       => "END",
            StoryNodeKind.Entry     => "ENTRY",
            StoryNodeKind.Exit      => "EXIT",
            StoryNodeKind.Logic     => "LOGIC",
            StoryNodeKind.Container => "CONTAINER",
            StoryNodeKind.PortalIn  => "PORTAL IN",
            StoryNodeKind.PortalOut => "PORTAL OUT",
            StoryNodeKind.LogicEntry   => "ENTRY",
            StoryNodeKind.LogicExit    => "EXIT",
            StoryNodeKind.Localization => "LOCALIZATION",
            StoryNodeKind.Icon         => "ICON",
            StoryNodeKind.LightDarkSwitch => "LIGHT / DARK",
            StoryNodeKind.SmartFormat      => "SMART FORMAT",
            StoryNodeKind.ExternalVariable => "EXTERNAL VARIABLE",
            StoryNodeKind.FlowText         => "FLOW TEXT",
            _                       => ""
        };

        public static string NodeColor(StoryNodeKind k) => k switch
        {
            StoryNodeKind.Start     => "var(--success)",
            StoryNodeKind.Entry     => "var(--success)",
            StoryNodeKind.End       => "var(--danger)",
            StoryNodeKind.Exit      => "var(--danger)",
            StoryNodeKind.Logic     => "var(--warning)",
            StoryNodeKind.Container => "var(--info)",
            StoryNodeKind.PortalIn  => "var(--orange)",
            StoryNodeKind.PortalOut => "var(--orange)",
            StoryNodeKind.LogicEntry   => "var(--success)",
            StoryNodeKind.LogicExit    => "var(--danger)",
            StoryNodeKind.Localization => "var(--accent)",
            StoryNodeKind.Icon         => "var(--orange)",
            StoryNodeKind.LightDarkSwitch => "var(--info)",
            StoryNodeKind.SmartFormat      => "var(--purple)",
            StoryNodeKind.ExternalVariable => "var(--code-func)",
            StoryNodeKind.FlowText         => "var(--warning)",
            _                       => "var(--text-dim)"
        };
    }
}
