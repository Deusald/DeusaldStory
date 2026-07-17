using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A node that lives only inside a logic node's inner content graph. It defines a <b>constant literal text</b> — a
    /// value the author types — carried by its <see cref="OutPoint"/> (a <c>Text</c> port), which can be wired into any
    /// text input (a FlowText / Exit text input, a SmartFormat's Localization input, or a Randomized Instruction's
    /// Result Format input). Unlike a Localization / SmartFormat text source the value is returned <b>verbatim</b> — no
    /// SmartFormat pass runs over it — so placeholder tokens the author types (e.g. <c>{RandomResult}</c>) survive to be
    /// substituted by whatever consumes the wire.
    /// </summary>
    public class StoryConstantStringNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>The literal text carried by the output port (returned verbatim, never SmartFormatted here).</summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>The single constant output port (connects to any text input).</summary>
        public StoryConnectionPoint OutPoint { get; set; } = new() { Name = "Text" };
    }
}
