using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// One continuation a logic node offers in <see cref="StoryLogicExitMode.ManyPaths"/> or
    /// <see cref="StoryLogicExitMode.HubPaths"/>. Each choice has an author <see cref="Name"/>, a player-facing
    /// <see cref="Label"/> (empty → "Click here to continue…" / "To continue go to section …"), an optional App
    /// auto-resolution <see cref="Condition"/> (the locked <see cref="IsElse"/> fallback carries none), and its own
    /// outer <see cref="OuterFlowOut"/> port wired to the next node. Ids are stable across edits so wires and
    /// Gamebook section tokens survive.
    /// <para>
    /// In <see cref="StoryLogicExitMode.SinglePath"/> this type is unused: there the continuations are the
    /// enumeration of <see cref="StoryLogicNode.ChoiceDefinitions"/>, all riding the node's single
    /// <see cref="StoryLogicNode.SingleOut"/>.
    /// </para>
    /// </summary>
    public class StoryChoice
    {
        public Guid   Id   { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;

        /// <summary>The player-facing text for this choice — a localization key plus its formatting.</summary>
        public StoryTextConfig Label { get; set; } = new();

        /// <summary>The locked fallback choice taken when no earlier choice's condition matched (App auto-resolution).</summary>
        public bool IsElse { get; set; }

        /// <summary>App auto-resolution condition (null for the Else choice, or when the node isn't auto-resolving).</summary>
        public StoryConditionExpr? Condition { get; set; }

        /// <summary>This choice's own outer Flow output on the logic card, wired to the next node.</summary>
        public StoryConnectionPoint OuterFlowOut { get; set; } = new() { Name = "Flow" };
    }
}
