using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// An inner content node on a logic node's flow spine that <b>sets</b> the value of a story-wide
    /// <b>external</b> variable (a <see cref="StoryVariable"/>) — the ones tracked by physical game components
    /// (tokens on the board). Because the components track those values automatically, this node emits <b>no</b>
    /// printed-Gamebook instruction; in the App it assigns the value when flow passes through the node during play.
    /// The variable is referenced by its <see cref="StoryVariable.Id"/> so a rename doesn't break the link, and
    /// <see cref="Value"/> is one of that variable's declared <see cref="StoryVariable.PossibleValues"/>. Flow passes
    /// straight through <see cref="FlowIn"/> → <see cref="FlowOut"/>.
    /// </summary>
    public class StorySetExternalVariableNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>The chosen external variable's id (a <c>StoryVariable.Id</c>). Empty when nothing picked yet.</summary>
        public Guid SelectedVariableId { get; set; }

        /// <summary>The value assigned — one of the variable's <see cref="StoryVariable.PossibleValues"/>.</summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>Flow input — wired from a previous flow node's output.</summary>
        public StoryConnectionPoint FlowIn { get; set; } = new() { Name = "Flow" };

        /// <summary>Flow output — wired to the next flow node's input or an Exit.</summary>
        public StoryConnectionPoint FlowOut { get; set; } = new() { Name = "Flow" };
    }
}
