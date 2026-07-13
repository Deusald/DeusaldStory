using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// One branch of a <see cref="StoryChoiceNode"/>. It carries an author-facing <see cref="Name"/>, a
    /// <see cref="TextIn"/> port that accepts the player-facing choice text (a Localization or SmartFormat output),
    /// and a <see cref="FlowOut"/> port the story continues from when this choice is taken. A choice's
    /// <see cref="FlowOut"/> may be wired only to one of the logic node's Exit points. The <see cref="Id"/> is kept
    /// stable across edits so wires survive when the owning choice node is re-edited.
    /// </summary>
    public class StoryChoiceOption
    {
        public Guid   Id   { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;

        /// <summary>The choice's player-facing text — accepts a Localization or SmartFormat text output.</summary>
        public StoryConnectionPoint TextIn { get; set; } = new() { Name = "Text" };

        /// <summary>Flow output for this branch — wired to one of the logic node's Exit points.</summary>
        public StoryConnectionPoint FlowOut { get; set; } = new() { Name = "Flow" };
    }

    /// <summary>
    /// An inner content node on a logic node's flow spine that presents the player with a set of <b>choices</b>.
    /// Flow arriving at <see cref="FlowIn"/> stops here; each of the <see cref="Options"/> is a branch the story can
    /// take. In the App each option is a button that advances flow out of that option's <see cref="StoryChoiceOption.FlowOut"/>;
    /// in the printed Gamebook each option renders as a "<i>{choice text}</i> go to section {section}" line. Every
    /// option's flow-out is wired to one of the logic node's Exit points, so the choice branches the story out to the
    /// next section.
    /// </summary>
    public class StoryChoiceNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>Flow input — wired from the Entry's flow output or a previous flow node's output.</summary>
        public StoryConnectionPoint FlowIn { get; set; } = new() { Name = "Flow" };

        /// <summary>The choices offered here, in display order. Each is a separate branch out of the node.</summary>
        public List<StoryChoiceOption> Options { get; set; } = new();
    }
}
