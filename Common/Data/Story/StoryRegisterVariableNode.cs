using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// An inner content node on a logic node's flow spine that <b>registers</b> (claims) a physical storage slot
    /// for a new variable and, optionally, sets its initial value. It owns the variable's identity — its
    /// <see cref="Name"/>, <see cref="Description"/>, storage <see cref="Type"/> and <see cref="SlotIndex"/> — and
    /// is referenced elsewhere (Set/Unregister nodes and story text) by its <see cref="Id"/>. Flow arriving at
    /// <see cref="FlowIn"/> performs the registration, then continues out of <see cref="FlowOut"/>. The slot stays
    /// claimed until a matching <see cref="StoryUnregisterVariableNode"/> releases it, so the same physical slot
    /// can be reused by later variables.
    /// </summary>
    public class StoryRegisterVariableNode
    {
        public Guid   Id          { get; set; } = Guid.NewGuid();
        public double X           { get; set; }
        public double Y           { get; set; }
        public string Name        { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>Which physical component tracks this variable.</summary>
        public StorageVariableType Type { get; set; }

        /// <summary>The claimed slot index within its type (0 = A, 1 = B…); label via <see cref="StorageSlots.Label"/>.</summary>
        public int SlotIndex { get; set; }

        // ── Number/Dial value parameters (unused for String) ───────────────────

        /// <summary>Number only — whether the value is stored with a D6 or a number token.</summary>
        public NumberStorageMode Mode { get; set; }

        /// <summary>Number only — how many distinct outcomes this variable represents.</summary>
        public NumberValueCount ValueCount { get; set; } = NumberValueCount.Six;

        /// <summary>Number (token) / Dial — the value is set/drawn secretly (players don't learn it until told).</summary>
        public bool Secret { get; set; }

        /// <summary>How the initial value is assigned when registering (Unset = claim only, no instruction).</summary>
        public NumberAssignment Assignment { get; set; }

        /// <summary>The value used when <see cref="Assignment"/> is <see cref="NumberAssignment.SetSpecific"/>.</summary>
        public int SpecificValue { get; set; }

        /// <summary>Optional "gamebook condition" localization key used to phrase a reveal/branch on this variable. Empty = none.</summary>
        public Guid ConditionKeyId { get; set; }

        /// <summary>The value the App preview / Conditions use when a <see cref="StoryGetVariableNode"/> reading this variable leaves its own preview value blank (the App tracks the live value at play time).</summary>
        public string PreviewValue { get; set; } = string.Empty;

        /// <summary>Where this node's Gamebook instruction / App input field sits relative to the section text.</summary>
        public StorageInstructionPlacement Placement { get; set; }

        // ── String value parameters (unused for Number/Dial) ───────────────────

        /// <summary>String only — how the value is decided (claim only, a baked value, or player-entered).</summary>
        public StringValueMode StringMode { get; set; }

        /// <summary>String / <see cref="StringValueMode.Specific"/> — the baked value the player writes and the App fills.</summary>
        public string StringValue { get; set; } = string.Empty;

        /// <summary>String / <see cref="StringValueMode.PlayerInput"/> — whether the App input field accepts text or a number.</summary>
        public StringInputKind StringInputKind { get; set; }

        /// <summary>String / <see cref="StringValueMode.PlayerInput"/> — Text input port carrying the "what to write" instruction (a Localization output).</summary>
        public StoryConnectionPoint InstructionIn { get; set; } = new() { Name = "Instruction" };

        /// <summary>Flow input — wired from the Entry's flow output or a previous flow node's output.</summary>
        public StoryConnectionPoint FlowIn { get; set; } = new() { Name = "Flow" };

        /// <summary>Flow output — wired to the next flow node's input or an Exit.</summary>
        public StoryConnectionPoint FlowOut { get; set; } = new() { Name = "Flow" };
    }
}
