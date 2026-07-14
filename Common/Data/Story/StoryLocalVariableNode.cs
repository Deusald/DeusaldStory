using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A node that lives only inside a logic node's inner content graph. It defines a <b>local constant</b> — a
    /// named value the author types — whose <see cref="OutPoint"/> can be wired into a SmartFormat variables input
    /// (to format text with a fixed value) or a Condition's variable input. Because the value is a single constant
    /// it resolves the same in the App and the Gamebook and never dimensions Gamebook sections (unlike a story-wide
    /// External Variable, which fans out one section per possible value).
    /// </summary>
    public class StoryLocalVariableNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>The SmartFormat token name this value is supplied under (the <c>{Name}</c> placeholder it fills).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The constant value carried by the output port.</summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>The single output port carrying the constant value (connects to a SmartFormat variables input or a Condition variable input).</summary>
        public StoryConnectionPoint OutPoint { get; set; } = new() { Name = "Value" };
    }
}
