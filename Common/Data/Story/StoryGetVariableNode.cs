using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A node that lives only inside a logic node's inner content graph. It reads back an already-registered storage
    /// variable (a <see cref="StoryRegisterVariableNode"/>) so its value can be wired into a SmartFormat variables
    /// input or a Condition's variable input. The variable is referenced by the register node's
    /// <see cref="StoryRegisterVariableNode.Id"/> so a rename doesn't break the link; its SmartFormat token name can
    /// be overridden (<see cref="NameOverride"/>) to whatever the format string uses.
    /// <para>
    /// Its value is medium-dependent: in the <b>App</b> the live value is tracked as the story is played, so the
    /// editor preview substitutes the author-entered <see cref="PreviewValue"/>; in the <b>Gamebook</b> the value is
    /// unknown at print time, so its slot tag (e.g. <c>TA</c>/<c>NA</c>/<c>DA</c>) is emitted instead of a value —
    /// which also keeps it out of the section product (only story-wide External Variables dimension sections).
    /// </para>
    /// </summary>
    public class StoryGetVariableNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>The registered storage variable this reads (a <see cref="StoryRegisterVariableNode.Id"/>). Empty when nothing picked yet.</summary>
        public Guid RegisteredVariableId { get; set; }

        /// <summary>Optional SmartFormat token name override; empty falls back to the register node's own name.</summary>
        public string NameOverride { get; set; } = string.Empty;

        /// <summary>The value used in the App preview (the App tracks the live value at runtime); also used to evaluate Conditions in the preview.</summary>
        public string PreviewValue { get; set; } = string.Empty;

        /// <summary>The single output port carrying the variable's value (connects to a SmartFormat variables input or a Condition variable input).</summary>
        public StoryConnectionPoint OutPoint { get; set; } = new() { Name = "Value" };
    }
}
