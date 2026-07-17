using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// An inner content node on a logic node's flow spine that <b>sets</b> the value of an already-registered
    /// storage variable — the "register unset near the start, assign it later" flow (e.g. mark a place visited when
    /// the player reaches it). It references the owning <see cref="StoryRegisterVariableNode.Id"/> via
    /// <see cref="RegisteredVariableId"/> and carries the new value/assignment. Flow passes straight through
    /// <see cref="FlowIn"/> → <see cref="FlowOut"/>.
    /// </summary>
    public class StorySetVariableNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>Whether this sets a variable picked by id (<see cref="RegisteredVariableId"/>) or one named by the wire into <see cref="NameIn"/> (<see cref="RefType"/>).</summary>
        public StorageVariableRefMode RefMode { get; set; }

        /// <summary>The storage type the wired name must resolve to, when <see cref="RefMode"/> is <see cref="StorageVariableRefMode.ByType"/>.</summary>
        public StorageVariableType RefType { get; set; }

        /// <summary>
        /// <see cref="StorageVariableRefMode.ByType"/> only — a <c>CVariable</c> input carrying the <b>name</b> of the
        /// variable to set. Only constant sources wire in, so <see cref="StoryGraphValidator"/> can prove the name
        /// belongs to a variable of <see cref="RefType"/> that is registered on this path.
        /// </summary>
        public StoryConnectionPoint NameIn { get; set; } = new() { Name = "Name" };

        /// <summary>The registered variable this sets (a <see cref="StoryRegisterVariableNode.Id"/>), when <see cref="RefMode"/> is <see cref="StorageVariableRefMode.Specific"/>. Empty when nothing picked yet.</summary>
        public Guid RegisteredVariableId { get; set; }

        /// <summary>How the value is assigned (Unset clears it back to "not set").</summary>
        public NumberAssignment Assignment { get; set; } = NumberAssignment.SetSpecific;

        /// <summary>The value is set/drawn secretly.</summary>
        public bool Secret { get; set; }

        /// <summary>The value used when <see cref="Assignment"/> is <see cref="NumberAssignment.SetSpecific"/>.</summary>
        public int SpecificValue { get; set; }

        /// <summary>Where this node's Gamebook instruction / App input field sits relative to the section text.</summary>
        public StorageInstructionPlacement Placement { get; set; }

        // ── String value parameters (used when the target variable is String) ───

        /// <summary>String only — how the value is decided (clear it, a baked value, or player-entered).</summary>
        public StringValueMode StringMode { get; set; }

        /// <summary>String / <see cref="StringValueMode.Specific"/> — the baked value the player writes and the App fills.</summary>
        public string StringValue { get; set; } = string.Empty;

        /// <summary>String / <see cref="StringValueMode.PlayerInput"/> — whether the App input field accepts text or a number.</summary>
        public StringInputKind StringInputKind { get; set; }

        /// <summary>String / <see cref="StringValueMode.PlayerInput"/> — Text input port carrying the "what to write" instruction (a Localization output).</summary>
        public StoryConnectionPoint InstructionIn { get; set; } = new() { Name = "Instruction" };

        /// <summary>String / <see cref="StringValueMode.PlayerInput"/> — Text input port carrying the App input field's placeholder hint (a Localization output). Empty falls back to a default.</summary>
        public StoryConnectionPoint PlaceholderIn { get; set; } = new() { Name = "Placeholder" };

        /// <summary>
        /// String / <see cref="StringValueMode.PlayerInput"/> — a <b>multi-wire</b> Variable input whose sources become
        /// extra operands of <see cref="ValidationRule"/>, each referenced by its connection <c>FromPoint</c> id.
        /// </summary>
        public StoryConnectionPoint ValidationIn { get; set; } = new() { Name = "Validation" };

        /// <summary>String / <see cref="StringValueMode.PlayerInput"/> — smallest accepted entry length in characters (default 1).</summary>
        public int MinLength { get; set; } = 1;

        /// <summary>String / <see cref="StringValueMode.PlayerInput"/> — largest accepted entry length in characters (default 30).</summary>
        public int MaxLength { get; set; } = 30;

        /// <summary>String / <see cref="StringValueMode.PlayerInput"/> — localization key for the message shown when the entry is outside <see cref="MinLength"/>…<see cref="MaxLength"/>. Empty = a default message.</summary>
        public Guid LengthErrorKeyId { get; set; }

        /// <summary>String / <see cref="StringValueMode.PlayerInput"/> — an <b>App-only</b> validation rule the entry must satisfy (a boolean Group tree; null = no rule). Operands reference <see cref="StorageValidation.ThisEntryRef"/>, other register-node ids, and the outputs wired into <see cref="ValidationIn"/> (by connection <c>FromPoint</c> id). The Gamebook cannot enforce it.</summary>
        public StoryConditionExpr? ValidationRule { get; set; }

        /// <summary>String / <see cref="StringValueMode.PlayerInput"/> — localization key for the message shown when <see cref="ValidationRule"/> is not met. Empty = a default message.</summary>
        public Guid ValidationErrorKeyId { get; set; }

        /// <summary>Flow input — wired from a previous flow node's output.</summary>
        public StoryConnectionPoint FlowIn { get; set; } = new() { Name = "Flow" };

        /// <summary>Flow output — wired to the next flow node's input or an Exit.</summary>
        public StoryConnectionPoint FlowOut { get; set; } = new() { Name = "Flow" };
    }
}
