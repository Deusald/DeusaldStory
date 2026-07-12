using System;
using System.Collections.Generic;
using System.Linq;
using DeusaldStoryCommon;

namespace DeusaldStoryWeb
{
    /// <summary>Which half of the editor is active: authoring the graph or running it.</summary>
    public enum EditorMode
    {
        Edit,
        Play
    }

    /// <summary>
    /// One step in the editor's breadcrumb — the path of nested story containers the user has drilled into
    /// (e.g. <c>Base / Chapter 1 / Intro</c>). <see cref="Id"/> is <see cref="Guid.Empty"/> for the root.
    /// </summary>
    public sealed record EditorCrumb(Guid Id, string Name);

    /// <summary>Visual/semantic kind of a story node, mapped to a colour + label in the graph.</summary>
    public enum StoryNodeKind
    {
        Start,      // the root container's single entry — where the story begins (green)
        End,        // the root container's single exit — where the story ends (red)
        Entry,      // a non-root container's entry point, rendered as a node inside it (green)
        Exit,       // a non-root container's exit point, rendered as a node inside it (red)
        Logic,      // a logic node — a stop point that generates content and runs calculations (yellow)
        Container   // a child container node (blue)
    }

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

        public List<EdPort> Inputs  { get; } = new();
        public List<EdPort> Outputs { get; } = new();
        public List<EdBlock> Blocks { get; } = new();
    }

    /// <summary>A directed connection between two nodes (output of <see cref="From"/> → input of <see cref="To"/>).</summary>
    public sealed class EdEdge
    {
        public Guid From { get; init; }
        public Guid To   { get; init; }
    }

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
