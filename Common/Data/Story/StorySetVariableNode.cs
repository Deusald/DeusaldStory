using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// An inner content node on a logic node's flow spine that <b>sets</b> the value of a global
    /// <see cref="StoryVariable"/> (External or Internal). It references the variable by its
    /// <see cref="StoryVariable.Id"/> so a rename doesn't break the link. Flow passes straight through
    /// <see cref="FlowIn"/> → <see cref="FlowOut"/>.
    /// <para>
    /// For an <b>External</b> variable the value follows <see cref="ExternalMode"/> — a fixed <see cref="ExternalValue"/>,
    /// the value of <see cref="ValueVariableId"/>, or that value translated through <see cref="ValueMap"/>. External
    /// sets emit no printed-Gamebook instruction (the components track the value).
    /// For an <b>Internal</b> variable the Number/Dial/String assignment fields below apply, and the Gamebook prints the
    /// instruction / the App shows the input field as before.
    /// </para>
    /// </summary>
    public class StorySetVariableNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>The global variable this sets (a <see cref="StoryVariable.Id"/>). Empty when nothing picked yet.</summary>
        public Guid SelectedVariableId { get; set; }

        // ── External-variable assignment ────────────────────────────────────────

        /// <summary>External only — how the assigned value is decided (a fixed value, wired-through, or wired-then-remapped).</summary>
        public StorySetExternalVariableMode ExternalMode { get; set; }

        /// <summary>External only — the value assigned in <see cref="StorySetExternalVariableMode.SpecificValue"/> mode.</summary>
        public string ExternalValue { get; set; } = string.Empty;

        /// <summary>External only — the conversion table used in <see cref="StorySetExternalVariableMode.RemapFromVariable"/> mode (incoming value → assigned value).</summary>
        public List<StorySetExternalVariableRemap> ValueMap { get; set; } = new();

        // ── Internal Number/Dial assignment ─────────────────────────────────────

        /// <summary>How the value is assigned (Unset clears it back to "not set").</summary>
        public NumberAssignment Assignment { get; set; } = NumberAssignment.SetSpecific;

        /// <summary>The value is set/drawn secretly.</summary>
        public bool Secret { get; set; }

        /// <summary>The value used when <see cref="Assignment"/> is <see cref="NumberAssignment.SetSpecific"/>.</summary>
        public int SpecificValue { get; set; }

        /// <summary>
        /// When the value is specific (<see cref="NumberAssignment.SetSpecific"/> for Number/Dial, or
        /// <see cref="StringValueMode.Specific"/> for String), take it from <see cref="ValueVariableId"/> (the value the
        /// App assigns at runtime) and <see cref="GamebookValueText"/> (the Gamebook display of what to write) instead of
        /// the baked <see cref="SpecificValue"/>/<see cref="StringValue"/>. The App stores the value silently either way,
        /// so only the Gamebook text differs.
        /// </summary>
        public bool WireValue { get; set; }

        /// <summary><see cref="WireValue"/> / external Map/Remap — the variable read for the value the App assigns at runtime.</summary>
        public Guid ValueVariableId { get; set; }

        /// <summary><see cref="WireValue"/> only — the Gamebook display of what to write.</summary>
        public StoryTextConfig GamebookValueText { get; set; } = new();

        /// <summary>When <see cref="Assignment"/> is <see cref="NumberAssignment.Randomize"/>, whether the App keeps the first draw across undo/redo (Saved) or re-draws each time (Pure). Unused otherwise and by the Gamebook.</summary>
        public RandomMode RandomMode { get; set; }

        /// <summary>Where this node's Gamebook instruction / App input field sits relative to the section text.</summary>
        public StorageInstructionPlacement Placement { get; set; }

        // ── String value parameters (used when the target variable is Internal Text) ───

        /// <summary>String only — how the value is decided (clear it, a baked value, or player-entered).</summary>
        public StringValueMode StringMode { get; set; }

        /// <summary>String / <see cref="StringValueMode.Specific"/> — the baked value the player writes and the App fills.</summary>
        public string StringValue { get; set; } = string.Empty;

        /// <summary>String / <see cref="StringValueMode.PlayerInput"/> — whether the App input field accepts text or a number.</summary>
        public StringInputKind StringInputKind { get; set; }

        /// <summary>String / <see cref="StringValueMode.PlayerInput"/> — the "what to write" instruction shown to the player.</summary>
        public StoryTextConfig Instruction { get; set; } = new();

        /// <summary>String / <see cref="StringValueMode.PlayerInput"/> — the App input field's placeholder hint. Empty falls back to a default.</summary>
        public StoryTextConfig Placeholder { get; set; } = new();

        /// <summary>String / <see cref="StringValueMode.PlayerInput"/> — smallest accepted entry length in characters (default 1).</summary>
        public int MinLength { get; set; } = 1;

        /// <summary>String / <see cref="StringValueMode.PlayerInput"/> — largest accepted entry length in characters (default 30).</summary>
        public int MaxLength { get; set; } = 30;

        /// <summary>String / <see cref="StringValueMode.PlayerInput"/> — localization key for the message shown when the entry is outside <see cref="MinLength"/>…<see cref="MaxLength"/>. Empty = a default message.</summary>
        public Guid LengthErrorKeyId { get; set; }

        /// <summary>String / <see cref="StringValueMode.PlayerInput"/> — an <b>App-only</b> validation rule the entry must satisfy (a boolean Group tree; null = no rule). Operands reference <see cref="StorageValidation.ThisEntryRef"/> or any variable id. The Gamebook cannot enforce it.</summary>
        public StoryConditionExpr? ValidationRule { get; set; }

        /// <summary>String / <see cref="StringValueMode.PlayerInput"/> — localization key for the message shown when <see cref="ValidationRule"/> is not met. Empty = a default message.</summary>
        public Guid ValidationErrorKeyId { get; set; }

        /// <summary>Flow input — wired from a previous flow node's output.</summary>
        public StoryConnectionPoint FlowIn { get; set; } = new() { Name = "Flow" };

        /// <summary>Flow output — wired to the next flow node's input or an Exit.</summary>
        public StoryConnectionPoint FlowOut { get; set; } = new() { Name = "Flow" };
    }
}
