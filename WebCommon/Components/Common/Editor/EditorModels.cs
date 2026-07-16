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

    // PortType moved to DeusaldStoryCommon (Common/Data/Story/PortType.cs) so persisted data — a Function
    // blueprint's signature ports — can carry a port type. Referenced here via `using DeusaldStoryCommon`.

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
        LogicEntry, // inside a logic node: the single Entry node (Title/Icon inputs + LFlow output) (green)
        LogicExit,  // inside a logic node: the single Exit node carrying the node's choices (LFlow input) (red)
        Localization, // inside a logic node: picks a localization key, emits its text (accent)
        Icon,       // inside a logic node: picks a project icon, feeds an Icon input (orange)
        LightDarkSwitch, // inside a logic node: picks between two icons by render theme (info)
        SmartFormat,     // inside a logic node: formats a text with connected variable values (purple)
        ExternalVariable, // inside a logic node: picks a story variable, feeds a SmartFormat/Exit variables input (teal)
        GetVariable,      // inside a logic node: reads a registered storage variable — App value / Gamebook slot tag (teal)
        ConstantVariable, // inside a logic node: a named constant value fed into a SmartFormat/Exit input (teal)
        FlowText,         // inside a logic node: on the LFlow chain, renders a text block then continues (amber)
        SplitForApp,      // inside a logic node: on the LFlow chain, breaks the App render into a new "continue" page (purple)
        RegisterVariable,   // inside a logic node: on the LFlow chain, claims a storage slot for a new variable (green)
        SetVariable,        // inside a logic node: on the LFlow chain, sets an already-registered variable's value (blue)
        UnregisterVariable, // inside a logic node: on the LFlow chain, releases a registered variable and frees its slot (red)
        SetExternalVariable, // inside a logic node: on the LFlow chain, assigns a value to a story-wide external variable (blue)
        PrevExitVariable,        // inside a logic node: exposes the upstream node's declared variables as constants (teal)
        LogicPortalIn,           // inside a logic node: a value portal's single input node (Text/Icon/Variable arrives here) (orange)
        LogicPortalOut,          // inside a logic node: a value portal's output node (the value re-emerges here) (orange)
        ConditionFlow,           // inside a logic node: on the LFlow chain, injects an optional block of flow gated by a constant condition (pink)
        EndConditionFlow,        // inside a logic node: the paired terminator that closes a Condition node's injected block (pink)
        Comment,                 // in a container's graph or a logic node's inner graph: a free-text author note, no ports (dim)
        BlueprintInstance,       // in a container's graph: an instance referencing a Container/Logic blueprint (purple)
        FunctionInstance,        // inside a logic node: on the LFlow chain, an instance referencing a Function blueprint (purple)
        FunctionEntry,           // inside a function definition's inner graph: the Entry node (LFlow-out + signature inputs) (green)
        FunctionExit             // inside a function definition's inner graph: the Exit node (signature outputs + LFlow-in) (red)
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
                node.Outputs.Add(new EdPort { Id = ep.Id, Name = ep.FlowKind == StoryPointFlow.VFlow ? UiLang.T(Localization.Editor.Nodes.Ports.variables) : UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = ep.FlowKind == StoryPointFlow.VFlow ? PortType.VFlow : PortType.Flow });
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
                node.Inputs.Add(new EdPort { Id = xp.Id, Name = xp.FlowKind == StoryPointFlow.VFlow ? UiLang.T(Localization.Editor.Nodes.Ports.variables) : UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = xp.FlowKind == StoryPointFlow.VFlow ? PortType.VFlow : PortType.Flow });
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
                node.Inputs.Add(new EdPort { Id = logic.EntryPoint.Id, Name = logic.AcceptVariables ? UiLang.T(Localization.Editor.Nodes.Ports.variables) : UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = logic.AcceptVariables ? PortType.VFlow : PortType.Flow });

                if (logic.ExitMode == StoryLogicExitMode.SinglePath)
                    node.Outputs.Add(new EdPort { Id = logic.VFlowOut.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.continueLabel), Type = PortType.VFlow });
                else
                    node.Outputs.AddRange(logic.Choices.Select(c =>
                        new EdPort { Id = c.OuterFlowOut.Id, Name = string.IsNullOrWhiteSpace(c.Name) ? UiLang.T(Localization.Editor.Nodes.Ports.choice) : c.Name, Type = PortType.Flow }));
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
                node.Inputs.AddRange(child.EntryPoints.Select(p => new EdPort { Id = p.Id, Name = p.Name, Type = p.FlowKind == StoryPointFlow.VFlow ? PortType.VFlow : PortType.Flow }));
                node.Outputs.AddRange(child.ExitPoints.Select(p => new EdPort { Id = p.Id, Name = p.Name, Type = p.FlowKind == StoryPointFlow.VFlow ? PortType.VFlow : PortType.Flow }));
                nodes.Add(node);
            }

            foreach (Guid instanceId in container.Instances)
            {
                if (!project.BlueprintInstances.TryGetValue(instanceId, out StoryBlueprintInstanceNode? inst)) continue;

                bool found = project.Blueprints.TryGetValue(inst.BlueprintId, out StoryBlueprint? bp);
                EdNode node = new()
                {
                    Id        = inst.Id,
                    Kind      = StoryNodeKind.BlueprintInstance,
                    Title     = found ? bp!.Name : UiLang.T(Localization.Editor.Nodes.Titles.missingBlueprint),
                    Subtitle  = found ? UiLang.T(Localization.Editor.Nodes.Titles.blueprintSubtitle) : UiLang.T(Localization.Editor.Nodes.Titles.deletedBlueprint),
                    X         = inst.X,
                    Y         = inst.Y,
                    Deletable = true
                };

                // Mirror the port types from the definition's current boundary, matched by DefinitionPointId.
                Dictionary<Guid, PortType> types = new();
                if (found)
                    foreach (BlueprintBoundaryPort b in StoryBlueprintBoundary.Enumerate(project, bp!))
                        types[b.DefinitionPointId] = b.Type;

                foreach (StoryBlueprintPortMap p in inst.EntryPorts)
                    node.Inputs.Add(new EdPort { Id = p.Id, Name = p.Name, Type = types.TryGetValue(p.DefinitionPointId, out PortType te) ? te : PortType.Flow });
                foreach (StoryBlueprintPortMap p in inst.ExitPorts)
                    node.Outputs.Add(new EdPort { Id = p.Id, Name = p.Name, Type = types.TryGetValue(p.DefinitionPointId, out PortType tx) ? tx : PortType.Flow });

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
        /// <see cref="StoryLogicNode.EntryPoint"/>, with the extra Title/Subtitle/Icon config inputs), one Exit node per
        /// <see cref="StoryLogicNode.ExitPoints"/> entry, and every Localization/Icon content node. Names for
        /// content nodes are resolved from <paramref name="localization"/> and the project's image library.
        /// </summary>
        public static List<EdNode> BuildLogicNodes(StoryProject project, StoryLogicNode logic, LocProject? localization)
        {
            List<EdNode> nodes = new();

            // When this logic node is a Function blueprint's definition body, its Entry/Exit expose the typed signature
            // (LFlow + inputs / outputs + LFlow) instead of the story-screen Title/Choices ports.
            StoryBlueprint? funcBp = null;
            foreach (StoryBlueprint b in project.Blueprints.Values)
                if (b.Kind == StoryBlueprintKind.Function && b.DefinitionNodeId == logic.Id) { funcBp = b; break; }

            // ── Entry node (green) — flow starts here; Title/Subtitle/Icon feed its content. ──
            (double ex, double ey) = PointPos(logic.EntryPoint, _LOGIC_ENTRY_X, _LOGIC_ENTRY_Y);
            EdNode entry = new()
            {
                Id        = logic.EntryPoint.Id,
                Kind      = funcBp is null ? StoryNodeKind.LogicEntry : StoryNodeKind.FunctionEntry,
                Title     = funcBp is null ? UiLang.T(Localization.Editor.Nodes.Titles.entry) : UiLang.T(Localization.Editor.Nodes.Titles.functionIn),
                X         = ex,
                Y         = ey,
                Deletable = false,
                Editable  = funcBp is not null
            };
            if (funcBp is null)
            {
                entry.Inputs.Add(new EdPort { Id = logic.TitleIn.Id,    Name = UiLang.T(Localization.Editor.Nodes.Ports.title),    Type = PortType.Text });
                entry.Inputs.Add(new EdPort { Id = logic.SubtitleIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.subtitle), Type = PortType.Text });
                entry.Inputs.Add(new EdPort { Id = logic.IconIn.Id,     Name = UiLang.T(Localization.Editor.Nodes.Ports.icon),     Type = PortType.Icon });
            }
            entry.Outputs.Add(new EdPort { Id = logic.EntryPoint.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = PortType.LFlow });
            if (funcBp is not null)
                foreach (StorySignaturePort input in funcBp.Inputs)
                    entry.Outputs.Add(new EdPort { Id = input.Id, Name = input.Name, Type = input.Type });
            nodes.Add(entry);

            // ── Exit node (red) — the single terminator carrying the node's choices. One LFlow input, one Text input
            //    per choice, one Variables input (wire it to auto-resolve the choice in the App), and — in auto mode —
            //    a single Auto-text input overriding the default "Click here to continue…" label. ──
            bool autoMode = funcBp is null && logic.ContentConnections.Exists(c => c.ToPoint == logic.ExitVariablesIn.Id);
            (double xx, double xy) = PointPos(logic.ExitLFlowIn, _LOGIC_EXIT_X, _LOGIC_EXIT_Y0);
            EdNode exit = new()
            {
                Id        = logic.ExitLFlowIn.Id,
                Kind      = funcBp is null ? StoryNodeKind.LogicExit : StoryNodeKind.FunctionExit,
                Title     = funcBp is null ? UiLang.T(Localization.Editor.Nodes.Titles.exit) : UiLang.T(Localization.Editor.Nodes.Titles.functionOut),
                Subtitle  = autoMode ? UiLang.T(Localization.Editor.Nodes.Titles.exitAutoSubtitle) : "",
                X         = xx,
                Y         = xy,
                Deletable = false,
                Editable  = funcBp is null
            };
            if (funcBp is not null)
                foreach (StorySignaturePort output in funcBp.Outputs)
                    exit.Inputs.Add(new EdPort { Id = output.Id, Name = output.Name, Type = output.Type });
            exit.Inputs.Add(new EdPort { Id = logic.ExitLFlowIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = PortType.LFlow });
            if (funcBp is null)
            {
                exit.Inputs.Add(new EdPort { Id = logic.ExitVariablesIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.variables), Type = PortType.Variable });
                if (autoMode)
                    exit.Inputs.Add(new EdPort { Id = logic.ExitAutoTextIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.text), Type = PortType.Text });
                foreach (StoryChoice choice in logic.Choices)
                {
                    string label = string.IsNullOrWhiteSpace(choice.Name) ? UiLang.T(Localization.Editor.Nodes.Ports.choice) : choice.Name;
                    exit.Inputs.Add(new EdPort { Id = choice.TextIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.choiceText, new Dictionary<string, object> { ["label"] = label }), Type = PortType.Text });
                }
            }
            nodes.Add(exit);

            // ── Localization nodes (accent) — show the full category/key path + preview text. ──
            foreach (StoryLocalizationNode loc in logic.LocalizationNodes)
            {
                LocLocalizationKey? key = localization?.Keys.Find(k => k.Id == loc.SelectedKeyId);
                EdNode node = new()
                {
                    Id        = loc.Id,
                    Kind      = StoryNodeKind.Localization,
                    Title     = key is not null ? FullKeyName(key, localization!) : UiLang.T(Localization.Editor.Nodes.Titles.noKey),
                    Subtitle  = key is not null ? PreviewText(key, localization) : "",
                    X         = loc.X,
                    Y         = loc.Y,
                    Deletable = true
                };
                node.Outputs.Add(new EdPort { Id = loc.OutPoint.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.text), Type = PortType.Text });
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
                    Title     = found ? image!.Name : UiLang.T(Localization.Editor.Nodes.Titles.noIcon),
                    Thumb     = found ? image!.Data : null,
                    X         = ico.X,
                    Y         = ico.Y,
                    Deletable = true
                };
                node.Outputs.Add(new EdPort { Id = ico.OutPoint.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.icon), Type = PortType.Icon });
                nodes.Add(node);
            }

            // ── Light/Dark switch nodes (info) — two icon inputs, one icon output; no config to edit. ──
            foreach (StoryLightDarkSwitchNode lds in logic.LightDarkSwitchNodes)
            {
                EdNode node = new()
                {
                    Id        = lds.Id,
                    Kind      = StoryNodeKind.LightDarkSwitch,
                    Title     = UiLang.T(Localization.Editor.Nodes.Titles.lightDark),
                    X         = lds.X,
                    Y         = lds.Y,
                    Deletable = true,
                    Editable  = false
                };
                node.Inputs.Add(new EdPort { Id = lds.DarkIn.Id,  Name = UiLang.T(Localization.Editor.Nodes.Ports.dark),  Type = PortType.Icon });
                node.Inputs.Add(new EdPort { Id = lds.LightIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.light), Type = PortType.Icon });
                node.Outputs.Add(new EdPort { Id = lds.OutPoint.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.icon), Type = PortType.Icon });
                nodes.Add(node);
            }

            // ── SmartFormat nodes (purple) — a text input, a many-to-one variables input, one formatted output. ──
            foreach (StorySmartFormatNode sf in logic.SmartFormatNodes)
            {
                EdNode node = new()
                {
                    Id        = sf.Id,
                    Kind      = StoryNodeKind.SmartFormat,
                    Title     = UiLang.T(Localization.Editor.Nodes.Titles.smartFormat),
                    X         = sf.X,
                    Y         = sf.Y,
                    Deletable = true,
                    Editable  = false
                };
                node.Inputs.Add(new EdPort { Id = sf.LocalizationIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.text),      Type = PortType.Text });
                node.Inputs.Add(new EdPort { Id = sf.VariablesIn.Id,    Name = UiLang.T(Localization.Editor.Nodes.Ports.variables), Type = PortType.Variable });
                node.Outputs.Add(new EdPort { Id = sf.OutPoint.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.text), Type = PortType.Text });
                nodes.Add(node);
            }

            // ── External Variable nodes (teal) — reference a story variable, feed a SmartFormat variables input. ──
            foreach (StoryExternalVariableNode ev in logic.ExternalVariableNodes)
            {
                StoryVariable? variable = StoryBuiltInVariables.Find(ev.SelectedVariableId)
                    ?? (project.Variables.TryGetValue(ev.SelectedVariableId, out StoryVariable? stored) ? stored : null);
                bool found = variable is not null;
                EdNode node = new()
                {
                    Id        = ev.Id,
                    Kind      = StoryNodeKind.ExternalVariable,
                    Title     = found ? variable!.Name : UiLang.T(Localization.Editor.Nodes.Titles.noVariable),
                    Subtitle  = found ? variable!.Description : "",
                    X         = ev.X,
                    Y         = ev.Y,
                    Deletable = true
                };
                bool constant = variable is not null && (variable.IsConstant || StoryBuiltInVariables.IsBuiltIn(variable.Id));
                node.Outputs.Add(new EdPort { Id = ev.OutPoint.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.variable), Type = constant ? PortType.CVariable : PortType.Variable });
                nodes.Add(node);
            }

            // ── Get Variable nodes (teal) — read a registered storage variable: a Value (App) and a Slot tag (Gamebook). ──
            foreach (StoryGetVariableNode gv in logic.GetVariableNodes)
            {
                StoryRegisterVariableNode? reg = FindRegister(project, gv.RegisteredVariableId);
                bool   found = reg is not null;
                string name  = !string.IsNullOrWhiteSpace(gv.NameOverride) ? gv.NameOverride
                             : found ? reg!.Name : "";
                EdNode node = new()
                {
                    Id        = gv.Id,
                    Kind      = StoryNodeKind.GetVariable,
                    Title     = string.IsNullOrWhiteSpace(name) ? UiLang.T(Localization.Editor.Nodes.Titles.noVariable) : name,
                    Subtitle  = found ? UiLang.T(Localization.Editor.Nodes.Titles.slotTypeSubtitle, new Dictionary<string, object> { ["slot"] = StorageSlots.Label(reg!.Type, reg.SlotIndex), ["type"] = reg.Type }) : UiLang.T(Localization.Editor.Nodes.Titles.unregistered),
                    X         = gv.X,
                    Y         = gv.Y,
                    Deletable = true
                };
                node.Outputs.Add(new EdPort { Id = gv.OutPoint.Id,     Name = UiLang.T(Localization.Editor.Nodes.Ports.value), Type = PortType.Variable });
                node.Outputs.Add(new EdPort { Id = gv.SlotOutPoint.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.slot),  Type = PortType.CVariable });
                nodes.Add(node);
            }

            // ── Constant Variable nodes (teal) — a named constant value fed into a SmartFormat/Exit input. ──
            foreach (StoryConstantVariableNode cv in logic.ConstantVariableNodes)
            {
                EdNode node = new()
                {
                    Id        = cv.Id,
                    Kind      = StoryNodeKind.ConstantVariable,
                    Title     = string.IsNullOrWhiteSpace(cv.Name) ? UiLang.T(Localization.Common.Placeholders.unnamed) : cv.Name,
                    Subtitle  = cv.Value,
                    X         = cv.X,
                    Y         = cv.Y,
                    Deletable = true
                };
                node.Outputs.Add(new EdPort { Id = cv.OutPoint.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.value), Type = PortType.CVariable });
                nodes.Add(node);
            }

            // ── FlowText nodes (amber) — sit on the flow spine, render a text block, continue flow. ──
            foreach (StoryFlowTextNode ft in logic.FlowTextNodes)
            {
                EdNode node = new()
                {
                    Id        = ft.Id,
                    Kind      = StoryNodeKind.FlowText,
                    Title     = UiLang.T(Localization.Editor.Nodes.Titles.flowText),
                    Subtitle  = FlowTextMediumLabel(ft) ?? "",
                    X         = ft.X,
                    Y         = ft.Y,
                    Deletable = true,
                    Editable  = true
                };
                node.Inputs.Add(new EdPort { Id = ft.FlowIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = PortType.LFlow });
                node.Inputs.Add(new EdPort { Id = ft.TextIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.text), Type = PortType.Text });
                node.Outputs.Add(new EdPort { Id = ft.FlowOut.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = PortType.LFlow });
                nodes.Add(node);
            }

            // ── Split-for-App nodes (purple) — on the flow spine, break the App render into a new "continue" page. ──
            foreach (StorySplitForAppNode split in logic.SplitForAppNodes)
            {
                EdNode node = new()
                {
                    Id        = split.Id,
                    Kind      = StoryNodeKind.SplitForApp,
                    Title     = UiLang.T(Localization.Editor.Nodes.Titles.splitForApp),
                    Subtitle  = UiLang.T(Localization.Editor.Nodes.Titles.splitForAppSubtitle),
                    X         = split.X,
                    Y         = split.Y,
                    Deletable = true,
                    Editable  = false
                };
                node.Inputs.Add(new EdPort { Id = split.FlowIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = PortType.LFlow });
                node.Outputs.Add(new EdPort { Id = split.FlowOut.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = PortType.LFlow });
                nodes.Add(node);
            }

            // ── Register-variable nodes (green) — on the flow spine, claim a storage slot for a new variable. ──
            foreach (StoryRegisterVariableNode reg in logic.RegisterVariableNodes)
            {
                EdNode node = new()
                {
                    Id        = reg.Id,
                    Kind      = StoryNodeKind.RegisterVariable,
                    Title     = string.IsNullOrWhiteSpace(reg.Name) ? UiLang.T(Localization.Common.Placeholders.unnamedVariable) : reg.Name,
                    Subtitle  = UiLang.T(Localization.Editor.Nodes.Titles.slotTypeSubtitle, new Dictionary<string, object> { ["slot"] = StorageSlots.Label(reg.Type, reg.SlotIndex), ["type"] = reg.Type }),
                    X         = reg.X,
                    Y         = reg.Y,
                    Deletable = true
                };
                node.Inputs.Add(new EdPort { Id = reg.FlowIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = PortType.LFlow });
                if (reg.Type == StorageVariableType.String && reg.StringMode == StringValueMode.PlayerInput)
                {
                    node.Inputs.Add(new EdPort { Id = reg.InstructionIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.instruction), Type = PortType.Text });
                    node.Inputs.Add(new EdPort { Id = reg.PlaceholderIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.placeholder), Type = PortType.Text });
                }
                node.Outputs.Add(new EdPort { Id = reg.FlowOut.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = PortType.LFlow });
                nodes.Add(node);
            }

            // ── Set-variable nodes (blue) — on the flow spine, set an already-registered variable's value. ──
            foreach (StorySetVariableNode set in logic.SetVariableNodes)
            {
                StoryRegisterVariableNode? target = FindRegister(project, set.RegisteredVariableId);
                EdNode node = new()
                {
                    Id        = set.Id,
                    Kind      = StoryNodeKind.SetVariable,
                    Title     = target is not null ? UiLang.T(Localization.Editor.Nodes.Titles.setVariable, new Dictionary<string, object> { ["name"] = NameOf(target) }) : UiLang.T(Localization.Editor.Nodes.Titles.setNoVariable),
                    Subtitle  = target is not null ? StorageSlots.Label(target.Type, target.SlotIndex) : "",
                    X         = set.X,
                    Y         = set.Y,
                    Deletable = true
                };
                node.Inputs.Add(new EdPort { Id = set.FlowIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = PortType.LFlow });
                if (target is { Type: StorageVariableType.String } && set.StringMode == StringValueMode.PlayerInput)
                {
                    node.Inputs.Add(new EdPort { Id = set.InstructionIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.instruction), Type = PortType.Text });
                    node.Inputs.Add(new EdPort { Id = set.PlaceholderIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.placeholder), Type = PortType.Text });
                }
                node.Outputs.Add(new EdPort { Id = set.FlowOut.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = PortType.LFlow });
                nodes.Add(node);
            }

            // ── Unregister-variable nodes (red) — on the flow spine, release a registered variable and free its slot. ──
            foreach (StoryUnregisterVariableNode unreg in logic.UnregisterVariableNodes)
            {
                StoryRegisterVariableNode? target = FindRegister(project, unreg.RegisteredVariableId);
                EdNode node = new()
                {
                    Id        = unreg.Id,
                    Kind      = StoryNodeKind.UnregisterVariable,
                    Title     = target is not null ? UiLang.T(Localization.Editor.Nodes.Titles.unregisterVariable, new Dictionary<string, object> { ["name"] = NameOf(target) }) : UiLang.T(Localization.Editor.Nodes.Titles.unregisterNoVariable),
                    Subtitle  = target is not null ? StorageSlots.Label(target.Type, target.SlotIndex) : "",
                    X         = unreg.X,
                    Y         = unreg.Y,
                    Deletable = true
                };
                node.Inputs.Add(new EdPort { Id = unreg.FlowIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = PortType.LFlow });
                node.Outputs.Add(new EdPort { Id = unreg.FlowOut.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = PortType.LFlow });
                nodes.Add(node);
            }

            // ── Set-external-variable nodes (blue) — on the flow spine, assign a value to a story-wide external variable. ──
            foreach (StorySetExternalVariableNode se in logic.SetExternalVariableNodes)
            {
                bool     found    = project.Variables.TryGetValue(se.SelectedVariableId, out StoryVariable? variable);
                bool     remapped = se.Mode == StorySetExternalVariableMode.RemapFromVariable;
                bool     mapped   = remapped || se.Mode == StorySetExternalVariableMode.MapFromVariable;
                EdNode node = new()
                {
                    Id        = se.Id,
                    Kind      = StoryNodeKind.SetExternalVariable,
                    Title     = found ? UiLang.T(Localization.Editor.Nodes.Titles.setExternal, new Dictionary<string, object> { ["name"] = variable!.Name }) : UiLang.T(Localization.Editor.Nodes.Titles.setExternalNoVariable),
                    Subtitle  = remapped ? UiLang.T(Localization.Editor.Nodes.Titles.setExternalRemapped) : mapped ? UiLang.T(Localization.Editor.Nodes.Titles.setExternalMapped) : string.IsNullOrEmpty(se.Value) ? "" : UiLang.T(Localization.Editor.Nodes.Titles.setExternalValue, new Dictionary<string, object> { ["value"] = se.Value }),
                    X         = se.X,
                    Y         = se.Y,
                    Deletable = true
                };
                node.Inputs.Add(new EdPort { Id = se.FlowIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = PortType.LFlow });
                if (mapped) node.Inputs.Add(new EdPort { Id = se.ValueIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.value), Type = PortType.Variable });
                node.Outputs.Add(new EdPort { Id = se.FlowOut.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = PortType.LFlow });
                nodes.Add(node);
            }

            // ── Prev Exit Variable node (teal, non-deletable) — appears when this node accepts variables and its
            //    Variables input is wired to an upstream Single-path node. Exposes each incoming declared variable as a
            //    Constant (CVariable) output, keyed by the upstream declared-variable id. ──
            List<StoryDeclaredVariable> incoming = logic.AcceptVariables
                ? StorySelectionResolver.IncomingVariables(project, logic)
                : new List<StoryDeclaredVariable>();
            if (incoming.Count > 0)
            {
                StoryPrevExitVariableNode prev = logic.PrevExitVariable;
                double px = prev.X == 0 && prev.Y == 0 ? _LOGIC_ENTRY_X : prev.X;
                double py = prev.X == 0 && prev.Y == 0 ? _PREV_EXIT_Y   : prev.Y;
                EdNode node = new()
                {
                    Id        = prev.Id,
                    Kind      = StoryNodeKind.PrevExitVariable,
                    Title     = UiLang.T(Localization.Editor.Nodes.Titles.prevExitVariables),
                    Subtitle  = string.Join(", ", incoming.Select(d => d.Name)),
                    X         = px,
                    Y         = py,
                    Deletable = false,
                    Editable  = false
                };
                foreach (StoryDeclaredVariable dv in incoming)
                    node.Outputs.Add(new EdPort { Id = dv.Id, Name = string.IsNullOrWhiteSpace(dv.Name) ? UiLang.T(Localization.Editor.Nodes.Ports.varFallback) : dv.Name, Type = PortType.CVariable });
                nodes.Add(node);
            }

            // ── Logic portals (orange) — one-in / many-out value relays. The in accepts any value (Data); each out
            //    adopts the concrete type (Text/Icon/Variable/Constant) of whatever is wired into the in. ──
            foreach (StoryLogicPortalNode portal in logic.LogicPortalNodes)
            {
                PortType outType    = ResolvedPortalType(project, logic, portal);
                string   outLabel   = PortTypeLabel(outType);

                EdNode inNode = new()
                {
                    Id                = portal.InPoint.Id,
                    Kind              = StoryNodeKind.LogicPortalIn,
                    Title             = portal.Name,
                    Subtitle          = outType == PortType.Data ? UiLang.T(Localization.Editor.Nodes.Titles.portalAny) : outLabel,
                    X                 = portal.InPoint.X,
                    Y                 = portal.InPoint.Y,
                    Deletable         = true,
                    Editable          = true,
                    // One-in / many-out: the single in jumps to the first out (no next-jump — there is only one in).
                    PortalCrossTarget = portal.OutPoints.Count > 0 ? portal.OutPoints[0].Id : null
                };
                inNode.Inputs.Add(new EdPort { Id = portal.InPoint.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.data), Type = PortType.Data });
                nodes.Add(inNode);

                for (int x = 0; x < portal.OutPoints.Count; ++x)
                {
                    StoryConnectionPoint outPoint = portal.OutPoints[x];
                    EdNode outNode = new()
                    {
                        Id                = outPoint.Id,
                        Kind              = StoryNodeKind.LogicPortalOut,
                        Title             = portal.Name,
                        Subtitle          = outLabel,
                        X                 = outPoint.X,
                        Y                 = outPoint.Y,
                        Deletable         = true,
                        Editable          = true,
                        // The cross-jump lands on the single in; the next-jump cycles the other outs.
                        PortalCrossTarget = portal.InPoint.Id,
                        PortalNextTarget  = portal.OutPoints.Count > 1 ? portal.OutPoints[(x + 1) % portal.OutPoints.Count].Id : null
                    };
                    outNode.Outputs.Add(new EdPort { Id = outPoint.Id, Name = outLabel, Type = outType });
                    nodes.Add(outNode);
                }
            }

            // ── Condition-flow pairs (pink) — one object → two cards. The Condition card sits on the flow spine
            //    (Flow in → Continue out) and injects an optional block out its "Condition true" output up to the
            //    paired End condition card (Flow in only), which terminates the injected block. ──
            foreach (StoryConditionFlowNode cf in logic.ConditionFlowNodes)
            {
                EdNode condition = new()
                {
                    Id        = cf.Id,
                    Kind      = StoryNodeKind.ConditionFlow,
                    Title     = string.IsNullOrWhiteSpace(cf.Name) ? UiLang.T(Localization.Editor.Nodes.Titles.condition) : cf.Name,
                    Subtitle  = cf.Negate ? UiLang.T(Localization.Editor.Nodes.Titles.conditionNegated) : "",
                    X         = cf.X,
                    Y         = cf.Y,
                    Deletable = true,
                    Editable  = true
                };
                condition.Inputs.Add(new EdPort { Id = cf.FlowIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = PortType.LFlow });
                condition.Inputs.Add(new EdPort { Id = cf.VariablesIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.variables), Type = PortType.Variable });
                condition.Outputs.Add(new EdPort { Id = cf.ContinueOut.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.continueLabel), Type = PortType.LFlow });
                condition.Outputs.Add(new EdPort { Id = cf.ConditionTrueOut.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.conditionTrue), Type = PortType.LFlow });
                nodes.Add(condition);

                EdNode end = new()
                {
                    Id        = cf.EndId,
                    Kind      = StoryNodeKind.EndConditionFlow,
                    Title     = string.IsNullOrWhiteSpace(cf.Name) ? UiLang.T(Localization.Editor.Nodes.Titles.endCondition) : cf.Name,
                    Subtitle  = "",
                    X         = cf.EndX,
                    Y         = cf.EndY,
                    Deletable = true,
                    Editable  = false
                };
                end.Inputs.Add(new EdPort { Id = cf.EndFlowIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = PortType.LFlow });
                nodes.Add(end);
            }

            // ── Function instances (purple) — an inlined computation on the LFlow chain: LFlow + typed inputs in, typed outputs + LFlow out. ──
            foreach (StoryFunctionInstanceNode fi in logic.FunctionInstanceNodes)
            {
                bool found = project.Blueprints.TryGetValue(fi.BlueprintId, out StoryBlueprint? fbp) && fbp!.Kind == StoryBlueprintKind.Function;
                EdNode node = new()
                {
                    Id        = fi.Id,
                    Kind      = StoryNodeKind.FunctionInstance,
                    Title     = found ? fbp!.Name : UiLang.T(Localization.Editor.Nodes.Titles.missingFunction),
                    Subtitle  = found ? UiLang.T(Localization.Editor.Nodes.Titles.functionSubtitle) : UiLang.T(Localization.Editor.Nodes.Titles.deletedFunction),
                    X         = fi.X,
                    Y         = fi.Y,
                    Deletable = true,
                    Editable  = false
                };
                node.Inputs.Add(new EdPort { Id = fi.FlowIn.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = PortType.LFlow });
                foreach (StoryBlueprintPortMap pm in fi.InputPorts)
                    node.Inputs.Add(new EdPort { Id = pm.Id, Name = pm.Name, Type = found ? SignatureType(fbp!.Inputs, pm.DefinitionPointId) : PortType.Data });
                foreach (StoryBlueprintPortMap pm in fi.OutputPorts)
                    node.Outputs.Add(new EdPort { Id = pm.Id, Name = pm.Name, Type = found ? SignatureType(fbp!.Outputs, pm.DefinitionPointId) : PortType.Data });
                node.Outputs.Add(new EdPort { Id = fi.FlowOut.Id, Name = UiLang.T(Localization.Editor.Nodes.Ports.flow), Type = PortType.LFlow });
                nodes.Add(node);
            }

            // ── Comment notes (dim, portless) — free-text author notes; ignored during playback. ──
            foreach (StoryCommentNode comment in logic.CommentNodes)
                nodes.Add(BuildCommentNode(comment));

            return nodes;
        }

        /// <summary>The port type of a function signature port by its definition id, or Data when it no longer exists.</summary>
        private static PortType SignatureType(List<StorySignaturePort> signature, Guid definitionPointId) =>
            signature.Find(p => p.Id == definitionPointId)?.Type ?? PortType.Data;

        /// <summary>
        /// The concrete port type a logic portal carries — the type of the output wired into its in (following any
        /// chained portals), or <see cref="PortType.Data"/> when the in is unconnected (so the out accepts nothing yet).
        /// </summary>
        public static PortType ResolvedPortalType(StoryProject project, StoryLogicNode logic, StoryLogicPortalNode portal)
        {
            Guid feed = logic.ContentConnections.Find(c => c.ToPoint == portal.InPoint.Id)?.FromPoint ?? Guid.Empty;
            Guid src  = logic.ResolvePortalSource(feed);
            return PortTypeOfOutput(project, logic, src) ?? PortType.Data;
        }

        /// <summary>The port type of an inner-graph output point (Text/Icon/Variable/Constant), or null when it names no known output.</summary>
        public static PortType? PortTypeOfOutput(StoryProject project, StoryLogicNode logic, Guid outputId)
        {
            if (outputId == Guid.Empty) return null;

            if (logic.LocalizationNodes.Exists(n => n.OutPoint.Id == outputId)
             || logic.SmartFormatNodes.Exists(n => n.OutPoint.Id == outputId))
                return PortType.Text;

            if (logic.IconNodes.Exists(n => n.OutPoint.Id == outputId)
             || logic.LightDarkSwitchNodes.Exists(n => n.OutPoint.Id == outputId))
                return PortType.Icon;

            if (logic.ExternalVariableNodes.Find(n => n.OutPoint.Id == outputId) is StoryExternalVariableNode ev)
            {
                StoryVariable? v = StoryBuiltInVariables.Find(ev.SelectedVariableId)
                    ?? (project.Variables.TryGetValue(ev.SelectedVariableId, out StoryVariable? stored) ? stored : null);
                bool constant = v is not null && (v.IsConstant || StoryBuiltInVariables.IsBuiltIn(v.Id));
                return constant ? PortType.CVariable : PortType.Variable;
            }

            if (logic.GetVariableNodes.Exists(n => n.OutPoint.Id == outputId))     return PortType.Variable;
            if (logic.GetVariableNodes.Exists(n => n.SlotOutPoint.Id == outputId)) return PortType.CVariable;
            if (logic.ConstantVariableNodes.Exists(n => n.OutPoint.Id == outputId)) return PortType.CVariable;
            if (StorySelectionResolver.IncomingVariables(project, logic).Exists(d => d.Id == outputId)) return PortType.CVariable;

            // A Function instance's typed output port carries the type of its signature output.
            foreach (StoryFunctionInstanceNode fi in logic.FunctionInstanceNodes)
                if (fi.OutputPorts.Find(p => p.Id == outputId) is StoryBlueprintPortMap op
                 && project.Blueprints.TryGetValue(fi.BlueprintId, out StoryBlueprint? fbp)
                 && fbp!.Kind == StoryBlueprintKind.Function)
                    return SignatureType(fbp.Outputs, op.DefinitionPointId);

            // Inside a Function definition graph, the signature inputs are outputs on the "Function in" node.
            foreach (StoryBlueprint b in project.Blueprints.Values)
                if (b.Kind == StoryBlueprintKind.Function && b.DefinitionNodeId == logic.Id
                 && b.Inputs.Find(s => s.Id == outputId) is StorySignaturePort sip)
                    return sip.Type;

            return null;
        }

        /// <summary>A short human label for a resolved port type (shown as a portal node's subtitle / port name).</summary>
        public static string PortTypeLabel(PortType t) => t switch
        {
            PortType.Text      => UiLang.T(Localization.Editor.Nodes.PortTypes.text),
            PortType.Icon      => UiLang.T(Localization.Editor.Nodes.PortTypes.icon),
            PortType.Variable  => UiLang.T(Localization.Editor.Nodes.PortTypes.variable),
            PortType.CVariable => UiLang.T(Localization.Editor.Nodes.PortTypes.constant),
            _                  => UiLang.T(Localization.Editor.Nodes.PortTypes.data)
        };

        /// <summary>A compact symbol for a condition operator (used in the Exit-node auto-resolution editor).</summary>
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

        /// <summary>Finds the Register-variable node with <paramref name="id"/> anywhere in the project's logic nodes.</summary>
        public static StoryRegisterVariableNode? FindRegister(StoryProject project, Guid id)
        {
            if (id == Guid.Empty) return null;
            foreach (StoryLogicNode logic in project.LogicNodes.Values)
            {
                StoryRegisterVariableNode? found = logic.RegisterVariableNodes.Find(n => n.Id == id);
                if (found is not null) return found;
            }
            return null;
        }

        /// <summary>A registered variable's display name, falling back to a placeholder when unnamed.</summary>
        private static string NameOf(StoryRegisterVariableNode reg) =>
            string.IsNullOrWhiteSpace(reg.Name) ? UiLang.T(Localization.Common.Placeholders.unnamedVariable) : reg.Name;

        /// <summary>
        /// Subtitle for a FlowText node — the non-Normal frame style and/or a medium restriction, joined with " · ".
        /// Null when the block renders in both mediums with the plain Normal frame (nothing worth flagging).
        /// </summary>
        private static string? FlowTextMediumLabel(StoryFlowTextNode ft)
        {
            string? medium = (ft.RenderInApp, ft.RenderInGamebook) switch
            {
                (true,  true)  => null,
                (true,  false) => UiLang.T(Localization.Editor.Nodes.Mediums.appOnly),
                (false, true)  => UiLang.T(Localization.Editor.Nodes.Mediums.gamebookOnly),
                (false, false) => UiLang.T(Localization.Editor.Nodes.Mediums.notRendered)
            };
            string? frame = ft.FrameStyle == StoryTextFrameStyle.Normal ? null : StoryStyle.FrameLabel(ft.FrameStyle);
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
            StoryNodeKind.Localization => UiLang.T(Localization.Editor.Nodes.Labels.localization),
            StoryNodeKind.Icon         => UiLang.T(Localization.Editor.Nodes.Labels.icon),
            StoryNodeKind.LightDarkSwitch => UiLang.T(Localization.Editor.Nodes.Labels.lightDark),
            StoryNodeKind.SmartFormat      => UiLang.T(Localization.Editor.Nodes.Labels.smartFormat),
            StoryNodeKind.ExternalVariable => UiLang.T(Localization.Editor.Nodes.Labels.externalVariable),
            StoryNodeKind.GetVariable      => UiLang.T(Localization.Editor.Nodes.Labels.getVariable),
            StoryNodeKind.ConstantVariable => UiLang.T(Localization.Editor.Nodes.Labels.constantVariable),
            StoryNodeKind.FlowText         => UiLang.T(Localization.Editor.Nodes.Labels.flowText),
            StoryNodeKind.SplitForApp      => UiLang.T(Localization.Editor.Nodes.Labels.splitForApp),
            StoryNodeKind.RegisterVariable   => UiLang.T(Localization.Editor.Nodes.Labels.registerVariable),
            StoryNodeKind.SetVariable        => UiLang.T(Localization.Editor.Nodes.Labels.setVariable),
            StoryNodeKind.UnregisterVariable => UiLang.T(Localization.Editor.Nodes.Labels.unregisterVariable),
            StoryNodeKind.SetExternalVariable => UiLang.T(Localization.Editor.Nodes.Labels.setExternalVariable),
            StoryNodeKind.PrevExitVariable        => UiLang.T(Localization.Editor.Nodes.Labels.prevExitVariables),
            StoryNodeKind.LogicPortalIn           => UiLang.T(Localization.Editor.Nodes.Labels.portalIn),
            StoryNodeKind.LogicPortalOut          => UiLang.T(Localization.Editor.Nodes.Labels.portalOut),
            StoryNodeKind.ConditionFlow           => UiLang.T(Localization.Editor.Nodes.Labels.condition),
            StoryNodeKind.EndConditionFlow        => UiLang.T(Localization.Editor.Nodes.Labels.endCondition),
            StoryNodeKind.Comment                 => UiLang.T(Localization.Editor.Nodes.Labels.comment),
            StoryNodeKind.BlueprintInstance       => UiLang.T(Localization.Editor.Nodes.Labels.blueprint),
            StoryNodeKind.FunctionInstance        => UiLang.T(Localization.Editor.Nodes.Labels.function),
            StoryNodeKind.FunctionEntry           => UiLang.T(Localization.Editor.Nodes.Labels.entry),
            StoryNodeKind.FunctionExit            => UiLang.T(Localization.Editor.Nodes.Labels.exit),
            _                       => ""
        };

        /// <summary>
        /// True when a node's <see cref="EdNode.Title"/> carries no information beyond the kind label already shown in
        /// the header (e.g. a SmartFormat / FlowText / Exit node whose title just repeats its type) — so callers can
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
            StoryNodeKind.Localization => "bi-translate",
            StoryNodeKind.Icon         => "bi-emoji-smile",
            StoryNodeKind.LightDarkSwitch => "bi-circle-half",
            StoryNodeKind.SmartFormat      => "bi-braces-asterisk",
            StoryNodeKind.ExternalVariable => "bi-braces",
            StoryNodeKind.GetVariable      => "bi-box-arrow-down",
            StoryNodeKind.ConstantVariable => "bi-braces",
            StoryNodeKind.FlowText         => "bi-text-paragraph",
            StoryNodeKind.SplitForApp      => "bi-scissors",
            StoryNodeKind.RegisterVariable   => "bi-box-seam",
            StoryNodeKind.SetVariable        => "bi-pencil-square",
            StoryNodeKind.UnregisterVariable => "bi-box-arrow-up",
            StoryNodeKind.SetExternalVariable => "bi-sliders",
            StoryNodeKind.PrevExitVariable        => "bi-braces",
            StoryNodeKind.LogicPortalIn           => "bi-box-arrow-in-right",
            StoryNodeKind.LogicPortalOut          => "bi-box-arrow-right",
            StoryNodeKind.ConditionFlow           => "bi-signpost-split",
            StoryNodeKind.EndConditionFlow        => "bi-sign-merge-left",
            StoryNodeKind.Comment                 => "bi-chat-left-text",
            StoryNodeKind.BlueprintInstance       => "bi-diagram-3",
            StoryNodeKind.FunctionInstance        => "bi-cpu",
            StoryNodeKind.FunctionEntry           => "bi-box-arrow-in-right",
            StoryNodeKind.FunctionExit            => "bi-box-arrow-right",
            _                       => "bi-circle"
        };

        /// <summary>
        /// The colour of a port's connection dot, keyed by what the port carries so a wire's signal type is readable
        /// at a glance: flow (blue), variable-flow (purple), logic-flow (amber), variable (red), constant (teal),
        /// text (green), icon (orange).
        /// </summary>
        public static string PortColor(PortType t) => t switch
        {
            PortType.Flow      => "var(--info)",
            PortType.VFlow     => "var(--purple)",
            PortType.LFlow     => "var(--warning)",
            PortType.Variable  => "var(--danger)",
            PortType.CVariable => "var(--code-func)",
            PortType.Text      => "var(--success)",
            PortType.Icon      => "var(--orange)",
            PortType.Data      => "var(--text-dim)",
            _                  => "var(--text-dim)"
        };

        /// <summary>
        /// Whether an output of type <paramref name="src"/> may wire into an input of type <paramref name="dst"/>.
        /// A <see cref="PortType.Variable"/> input accepts a Variable or a Constant (CVariable) source; a
        /// <see cref="PortType.CVariable"/> input accepts only a Constant source; every other type joins its own kind.
        /// </summary>
        public static bool IsCompatible(PortType src, PortType dst) => dst switch
        {
            PortType.Variable  => src is PortType.Variable or PortType.CVariable,
            PortType.CVariable => src is PortType.CVariable,
            // A portal's Data input accepts any value signal; two portals may also chain (Data → Data).
            PortType.Data      => src is PortType.Text or PortType.Icon or PortType.Variable or PortType.CVariable or PortType.Data,
            _                  => src == dst
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
            StoryNodeKind.GetVariable      => "var(--code-func)",
            StoryNodeKind.ConstantVariable => "var(--code-func)",
            StoryNodeKind.FlowText         => "var(--warning)",
            StoryNodeKind.SplitForApp      => "var(--purple)",
            StoryNodeKind.RegisterVariable   => "var(--success)",
            StoryNodeKind.SetVariable        => "var(--info)",
            StoryNodeKind.UnregisterVariable => "var(--danger)",
            StoryNodeKind.SetExternalVariable => "var(--info)",
            StoryNodeKind.PrevExitVariable        => "var(--code-func)",
            StoryNodeKind.LogicPortalIn           => "var(--orange)",
            StoryNodeKind.LogicPortalOut          => "var(--orange)",
            StoryNodeKind.ConditionFlow           => "var(--pink)",
            StoryNodeKind.EndConditionFlow        => "var(--pink)",
            StoryNodeKind.Comment                 => "var(--text-dim)",
            StoryNodeKind.BlueprintInstance       => "var(--purple)",
            StoryNodeKind.FunctionInstance        => "var(--purple)",
            StoryNodeKind.FunctionEntry           => "var(--success)",
            StoryNodeKind.FunctionExit            => "var(--danger)",
            _                       => "var(--text-dim)"
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
