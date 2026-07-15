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
    /// It exposes <b>two</b> output ports for the two mediums:
    /// <see cref="OutPoint"/> — a <c>Variable</c> port carrying the live value (App only; the preview substitutes
    /// <see cref="PreviewValue"/>); and <see cref="SlotOutPoint"/> — a <c>CVariable</c> port carrying the variable's
    /// <c>{slot}</c> tag (e.g. <c>TA</c>/<c>NA</c>/<c>DA</c>), so the Gamebook can print which slot to read. Wire the
    /// value port for App formatting and the slot port for Gamebook text.
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

        /// <summary>The value used in the App preview (the App tracks the live value at runtime); also used to evaluate conditions in the preview.</summary>
        public string PreviewValue { get; set; } = string.Empty;

        /// <summary>The <c>Variable</c> output carrying the live value (App only) — connects to a SmartFormat / Exit variables input.</summary>
        public StoryConnectionPoint OutPoint { get; set; } = new() { Name = "Value" };

        /// <summary>The <c>CVariable</c> output carrying the variable's <c>{slot}</c> tag (Gamebook) — connects to a SmartFormat / Exit variables input.</summary>
        public StoryConnectionPoint SlotOutPoint { get; set; } = new() { Name = "Slot" };
    }
}
