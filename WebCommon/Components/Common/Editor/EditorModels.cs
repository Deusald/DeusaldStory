using System;
using System.Collections.Generic;

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

    // ─────────────────────────────────────────────────────────────────────────
    // NOTE: The types below are editor-only VIEW MODELS used to drive the graph
    // and inspector while the feature is mocked. The runtime data model
    // (StoryContainerNode / StoryLogicNode) does not yet carry node kinds, tags,
    // positions or content blocks — expanding it is a separate step. Once it
    // does, the editor should project from StoryProject into these (or replace
    // them). Nothing here is persisted.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Visual/semantic kind of a story node, mapped to a colour + label in the graph.</summary>
    public enum StoryNodeKind
    {
        Start,
        Scene,
        Choice,
        End
    }

    /// <summary>Kind of an inspector content block.</summary>
    public enum StoryBlockKind
    {
        Narration,
        Dialogue,
        Choice
    }

    /// <summary>A node as drawn on the graph canvas (mock).</summary>
    public sealed class EdNode
    {
        public Guid          Id      { get; init; } = Guid.NewGuid();
        public StoryNodeKind Kind    { get; set; }
        public string        Tag     { get; set; } = "";
        public string        Title   { get; set; } = "";
        public string        Preview { get; set; } = "";
        public double        X       { get; set; }
        public double        Y       { get; set; }

        public List<EdBlock> Blocks { get; } = new();
    }

    /// <summary>A directed connection between two nodes (output of <see cref="From"/> → input of <see cref="To"/>).</summary>
    public sealed class EdEdge
    {
        public Guid From { get; init; }
        public Guid To   { get; init; }
    }

    /// <summary>A single content block shown in the inspector (mock).</summary>
    public sealed class EdBlock
    {
        public StoryBlockKind Kind    { get; init; }
        public string?        Speaker { get; init; }
        public string         Text    { get; init; } = "";
    }

    /// <summary>Maps the editor view-model kinds to their design-token colours and labels.</summary>
    public static class StoryStyle
    {
        public static string NodeLabel(StoryNodeKind k) => k switch
        {
            StoryNodeKind.Start  => "START",
            StoryNodeKind.Scene  => "SCENE",
            StoryNodeKind.Choice => "CHOICE",
            StoryNodeKind.End    => "END",
            _                    => ""
        };

        public static string NodeColor(StoryNodeKind k) => k switch
        {
            StoryNodeKind.Start  => "var(--success)",
            StoryNodeKind.Scene  => "var(--info)",
            StoryNodeKind.Choice => "var(--warning)",
            StoryNodeKind.End    => "var(--danger)",
            _                    => "var(--text-dim)"
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
