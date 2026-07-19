using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A node that lives only inside a logic node's inner content graph and sits on the flow spine. It defines a
    /// <b>constant</b> — a named value the author types — which is published into the node's variable dictionary
    /// under <see cref="Name"/> when flow reaches it, making <c>{Name}</c> available to every text rendered
    /// afterwards. Because it is a constant it resolves the same in the App and the Gamebook and never dimensions
    /// Gamebook sections.
    /// </summary>
    public class StoryConstantVariableNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>The SmartFormat token name this value is published under (the <c>{Name}</c> placeholder it fills).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The constant value published into the dictionary.</summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>Flow input — wired from the Entry or a previous spine node's flow output.</summary>
        public StoryConnectionPoint FlowIn { get; set; } = new() { Name = "Flow" };

        /// <summary>Flow output — wired to the next spine node's flow input or the Exit.</summary>
        public StoryConnectionPoint FlowOut { get; set; } = new() { Name = "Flow" };
    }
}
