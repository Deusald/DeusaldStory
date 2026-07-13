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

        /// <summary>The registered variable this sets (a <see cref="StoryRegisterVariableNode.Id"/>). Empty when nothing picked yet.</summary>
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

        /// <summary>Flow input — wired from a previous flow node's output.</summary>
        public StoryConnectionPoint FlowIn { get; set; } = new() { Name = "Flow" };

        /// <summary>Flow output — wired to the next flow node's input or an Exit.</summary>
        public StoryConnectionPoint FlowOut { get; set; } = new() { Name = "Flow" };
    }
}
