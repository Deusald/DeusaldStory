using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A node that lives only inside a logic node's inner content graph and sits on the <b>flow</b> spine: flow
    /// arriving at its <see cref="FlowIn"/> renders the text wired into its <see cref="TextIn"/> (a Localization or
    /// SmartFormat output), then continues out of its <see cref="FlowOut"/> to the next flow node. Chaining several
    /// FlowText nodes off the Entry's flow output is how a logic node renders an ordered sequence of text blocks.
    /// </summary>
    public class StoryFlowTextNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>Flow input — wired from the Entry's flow output or a previous FlowText's <see cref="FlowOut"/>.</summary>
        public StoryConnectionPoint FlowIn { get; set; } = new() { Name = "Flow" };

        /// <summary>The text this block renders — accepts a Localization or SmartFormat text output.</summary>
        public StoryConnectionPoint TextIn { get; set; } = new() { Name = "Text" };

        /// <summary>Flow output — wired to the next FlowText's <see cref="FlowIn"/> or an Exit.</summary>
        public StoryConnectionPoint FlowOut { get; set; } = new() { Name = "Flow" };
    }
}
