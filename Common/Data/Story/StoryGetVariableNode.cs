using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A node that lives only inside a logic node's inner content graph. It reads back a global
    /// <see cref="StoryVariable"/> (External or Internal) so its value can be wired into a SmartFormat variables input
    /// or a Condition's variable input. The variable is referenced by its <see cref="StoryVariable.Id"/> so a rename
    /// doesn't break the link; its SmartFormat token name can be overridden (<see cref="NameOverride"/>) to whatever
    /// the format string uses.
    /// <para>
    /// It exposes <b>two</b> output ports, each filling a <i>distinct</i> SmartFormat token so both can be wired into
    /// one format (e.g. behind a <c>Medium:choose(...)</c>): <see cref="OutPoint"/> — a <c>Variable</c> port carrying
    /// the live value (App; the preview substitutes <see cref="PreviewValue"/>, and Initial/Constant externals resolve
    /// to their fixed value in both mediums) under the token <c>{Name}</c>; and <see cref="SlotOutPoint"/> — a
    /// <c>CVariable</c> port present only for <b>Internal</b> variables, carrying the variable's slot tag (e.g.
    /// <c>TA</c>/<c>NA</c>/<c>DA</c>) under the token <c>{Name}Slot</c> (or <see cref="SlotNameOverride"/> when set), so
    /// the Gamebook can print which slot to read. Wire the value port for App formatting and the slot port for Gamebook text.
    /// </para>
    /// </summary>
    public class StoryGetVariableNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>The global variable this reads (a <see cref="StoryVariable.Id"/>, may be a built-in). Empty when nothing picked yet.</summary>
        public Guid SelectedVariableId { get; set; }

        /// <summary>Optional SmartFormat token name override; empty falls back to the variable's own name.</summary>
        public string NameOverride { get; set; } = string.Empty;

        /// <summary>Optional SmartFormat token name override for the <see cref="SlotOutPoint"/>; empty falls back to the value token plus a <c>Slot</c> suffix.</summary>
        public string SlotNameOverride { get; set; } = string.Empty;

        /// <summary>The value used in the App preview (the App tracks the live value at runtime); also used to evaluate conditions in the preview.</summary>
        public string PreviewValue { get; set; } = string.Empty;

        /// <summary>The <c>Variable</c> output carrying the live value — connects to a SmartFormat / Exit variables input.</summary>
        public StoryConnectionPoint OutPoint { get; set; } = new() { Name = "Value" };

        /// <summary>The <c>CVariable</c> output carrying the variable's slot tag under the token <c>{Name}Slot</c> (Internal only) — connects to a SmartFormat / Exit variables input.</summary>
        public StoryConnectionPoint SlotOutPoint { get; set; } = new() { Name = "Slot" };
    }
}
