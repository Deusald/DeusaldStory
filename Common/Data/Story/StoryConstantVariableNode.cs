using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A node that lives only inside a logic node's inner content graph. It defines a <b>constant</b> — a named value
    /// the author types — whose <see cref="OutPoint"/> (a <c>CVariable</c> port) can be wired into a SmartFormat
    /// variables input or an Exit node's variables input. Because it is a constant it is known before any Gamebook
    /// section is evaluated, resolves the same in the App and the Gamebook, and never dimensions Gamebook sections.
    /// </summary>
    public class StoryConstantVariableNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>The SmartFormat token name this value is supplied under (the <c>{Name}</c> placeholder it fills).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The constant value carried by the output port.</summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>The single constant output port (connects to a SmartFormat variables input or an Exit node variables input).</summary>
        public StoryConnectionPoint OutPoint { get; set; } = new() { Name = "Value" };
    }
}
