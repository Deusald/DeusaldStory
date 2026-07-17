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
    /// It exposes <b>two</b> output ports for the two mediums, each filling a <i>distinct</i> SmartFormat token so both
    /// can be wired into one format (e.g. behind an <c>AppGamebook:choose(...)</c>):
    /// <see cref="OutPoint"/> — a <c>Variable</c> port carrying the live value (App only; the preview substitutes
    /// <see cref="PreviewValue"/>) under the token <c>{Name}</c>; and <see cref="SlotOutPoint"/> — a <c>CVariable</c>
    /// port carrying the variable's slot tag (e.g. <c>TA</c>/<c>NA</c>/<c>DA</c>) under the token <c>{Name}Slot</c>
    /// (or <see cref="SlotNameOverride"/> when set), so
    /// the Gamebook can print which slot to read. Wire the value port for App formatting and the slot port for Gamebook text.
    /// </para>
    /// </summary>
    public class StoryGetVariableNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>Whether this reads a variable picked by id (<see cref="RegisteredVariableId"/>) or one named by the wire into <see cref="NameIn"/> (<see cref="RefType"/>).</summary>
        public StorageVariableRefMode RefMode { get; set; }

        /// <summary>The storage type the wired name must resolve to, when <see cref="RefMode"/> is <see cref="StorageVariableRefMode.ByType"/>.</summary>
        public StorageVariableType RefType { get; set; }

        /// <summary>
        /// <see cref="StorageVariableRefMode.ByType"/> only — a <c>CVariable</c> input carrying the <b>name</b> of the
        /// variable to read. Only constant sources wire in, so the name is known before any Gamebook section is built
        /// and <see cref="StoryGraphValidator"/> can prove it names a registered variable of <see cref="RefType"/>.
        /// </summary>
        public StoryConnectionPoint NameIn { get; set; } = new() { Name = "Name" };

        /// <summary>The registered storage variable this reads (a <see cref="StoryRegisterVariableNode.Id"/>), when <see cref="RefMode"/> is <see cref="StorageVariableRefMode.Specific"/>. Empty when nothing picked yet.</summary>
        public Guid RegisteredVariableId { get; set; }

        /// <summary>Optional SmartFormat token name override; empty falls back to the register node's own name.</summary>
        public string NameOverride { get; set; } = string.Empty;

        /// <summary>Optional SmartFormat token name override for the <see cref="SlotOutPoint"/>; empty falls back to the value token plus a <c>Slot</c> suffix.</summary>
        public string SlotNameOverride { get; set; } = string.Empty;

        /// <summary>The value used in the App preview (the App tracks the live value at runtime); also used to evaluate conditions in the preview.</summary>
        public string PreviewValue { get; set; } = string.Empty;

        /// <summary>The <c>Variable</c> output carrying the live value (App only) — connects to a SmartFormat / Exit variables input.</summary>
        public StoryConnectionPoint OutPoint { get; set; } = new() { Name = "Value" };

        /// <summary>The <c>CVariable</c> output carrying the variable's slot tag under the token <c>{Name}Slot</c> (Gamebook) — connects to a SmartFormat / Exit variables input.</summary>
        public StoryConnectionPoint SlotOutPoint { get; set; } = new() { Name = "Slot" };
    }
}
