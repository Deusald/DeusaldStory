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
    /// <see cref="Flow"/> (all container/logic-boundary ports); the logic node's Title/Icon config inputs and the
    /// matching content-node outputs use <see cref="Title"/>/<see cref="Icon"/>.
    /// </summary>
    public enum PortType
    {
        Flow,
        Title,
        Icon
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
        Localization, // inside a logic node: picks a localization key, feeds a Title input (accent)
        Icon        // inside a logic node: picks a project icon, feeds an Icon input (orange)
    }

    /// <summary>A point on the graph canvas in world (un-panned, un-scaled) coordinates.</summary>
    public readonly record struct CanvasPoint(double X, double Y);

    /// <summary>
    /// One selectable entry in the right-click node palette. <see cref="Kind"/> tells the editor which kind of
    /// node to create; the rest is presentation. The available set is context-dependent (see the palette).
    /// </summary>
    public sealed record NodePaletteItem(StoryNodeKind Kind, string Icon, string Label, string Description);

    /// <summary>Kind of an inspector content block.</summary>
    public enum StoryBlockKind
    {
        Narration,
        Dialogue,
        Choice
    }

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

        public List<EdPort> Inputs  { get; } = new();
        public List<EdPort> Outputs { get; } = new();
        public List<EdBlock> Blocks { get; } = new();
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

    /// <summary>A single content block shown in the inspector (mock — content authoring is a later step).</summary>
    public sealed class EdBlock
    {
        public StoryBlockKind Kind    { get; init; }
        public string?        Speaker { get; init; }
        public string         Text    { get; init; } = "";
    }

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
            entry.Inputs.Add(new EdPort { Id = logic.TitleIn.Id, Name = "Title", Type = PortType.Title });
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

            // ── Localization nodes (accent) — resolve the picked key's name + preview text. ──
            foreach (StoryLocalizationNode loc in logic.LocalizationNodes)
            {
                LocLocalizationKey? key = localization?.Keys.Find(k => k.Id == loc.SelectedKeyId);
                EdNode node = new()
                {
                    Id        = loc.Id,
                    Kind      = StoryNodeKind.Localization,
                    Title     = key is not null ? key.KeyName : "(no key)",
                    Subtitle  = key is not null ? PreviewText(key, localization) : "",
                    X         = loc.X,
                    Y         = loc.Y,
                    Deletable = true
                };
                node.Outputs.Add(new EdPort { Id = loc.OutPoint.Id, Name = "Text", Type = PortType.Title });
                nodes.Add(node);
            }

            // ── Icon nodes (orange) — resolve the picked icon's name. ──
            foreach (StoryIconNode ico in logic.IconNodes)
            {
                bool found = project.Images.TryGetValue(ico.SelectedImageId, out StoryImage? image);
                EdNode node = new()
                {
                    Id        = ico.Id,
                    Kind      = StoryNodeKind.Icon,
                    Title     = found ? image!.Name : "(no icon)",
                    X         = ico.X,
                    Y         = ico.Y,
                    Deletable = true
                };
                node.Outputs.Add(new EdPort { Id = ico.OutPoint.Id, Name = "Icon", Type = PortType.Icon });
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
            _                       => "var(--text-dim)"
        };

        public static string BlockLabel(StoryBlockKind k) => k switch
        {
            StoryBlockKind.Narration => "NARRATION",
            StoryBlockKind.Dialogue  => "DIALOGUE",
            StoryBlockKind.Choice    => "CHOICE",
            _                        => ""
        };

        public static string BlockColor(StoryBlockKind k) => k switch
        {
            StoryBlockKind.Narration => "var(--success)",
            StoryBlockKind.Dialogue  => "var(--info)",
            StoryBlockKind.Choice    => "var(--warning)",
            _                        => "var(--text-dim)"
        };
    }
}
