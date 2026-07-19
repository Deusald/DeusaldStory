using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>Whether a choice definition enumerates a variable's values/ranges, or an authored list of options.</summary>
    public enum StoryChoiceDefKind
    {
        Variable,
        Option
    }

    /// <summary>How a <see cref="StoryChoiceDefKind.Variable"/> definition enumerates its target variable.</summary>
    public enum StoryVariableChoiceMode
    {
        /// <summary>One entry per possible value; the concrete value is what lands in the Choice variable.</summary>
        ValueBased,

        /// <summary>One entry per authored <see cref="StoryChoiceRange"/>; its "from_to" token lands in the Choice variable.</summary>
        RangeBased
    }

    /// <summary>
    /// One from..to band of a <see cref="StoryVariableChoiceMode.RangeBased"/> definition, with its own label key.
    /// The key is SmartFormatted with a <c>Range</c> token whose value is <see cref="Token"/> (e.g. <c>0_3</c>).
    /// </summary>
    public class StoryChoiceRange
    {
        public Guid Id   { get; set; } = Guid.NewGuid();
        public int  From { get; set; }
        public int  To   { get; set; }

        /// <summary>The localization key labelling this band's button / section-jump line.</summary>
        public Guid KeyId { get; set; }

        /// <summary>The value this band pins into its Choice variable — also the <c>Range</c> token.</summary>
        public string Token => $"{From}_{To}";
    }

    /// <summary>
    /// One entry of a <see cref="StoryChoiceDefKind.Option"/> definition — either a manually written value, or the
    /// value of any variable. Labelling falls back in this order: this option's own <see cref="KeyId"/>, then (for a
    /// variable option) the target variable's <see cref="StoryVariable.OptionConditionKeyId"/>, then the literal
    /// <see cref="Text"/>.
    /// </summary>
    public class StoryChoiceOption
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>When set, this option's value is <see cref="SelectedVariableId"/>'s value; otherwise <see cref="Text"/>.</summary>
        public bool FromVariable { get; set; }

        /// <summary>The variable read when <see cref="FromVariable"/> is set (a <c>StoryVariable.Id</c>).</summary>
        public Guid SelectedVariableId { get; set; }

        /// <summary>The manually written value when <see cref="FromVariable"/> is clear.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>Optional per-option label key; overrides the target variable's option key when set.</summary>
        public Guid KeyId { get; set; }
    }

    /// <summary>
    /// One of a Single-path logic node's (at most <see cref="StoryChoiceVariables.MAX_DEFINITIONS"/>) choice
    /// definitions. Its <b>position</b> in <see cref="StoryLogicNode.ChoiceDefinitions"/> decides which Choice
    /// variable it writes (0 ⇒ ChoiceA, 1 ⇒ ChoiceB, 2 ⇒ ChoiceC). Enumerating every definition's entries is what
    /// produces this node's continuations and dimensions the downstream node's Gamebook sections.
    /// </summary>
    public class StoryChoiceDefinition
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public StoryChoiceDefKind Kind { get; set; } = StoryChoiceDefKind.Variable;

        /// <summary>Variable kind — the variable enumerated (a <c>StoryVariable.Id</c>, possibly a derived text map).</summary>
        public Guid SelectedVariableId { get; set; }

        /// <summary>Variable kind — every possible value, or the authored bands below.</summary>
        public StoryVariableChoiceMode VariableMode { get; set; } = StoryVariableChoiceMode.ValueBased;

        /// <summary>Variable / Range-based — the authored bands, each with its own label key.</summary>
        public List<StoryChoiceRange> Ranges { get; set; } = new();

        /// <summary>Option kind — the authored options.</summary>
        public List<StoryChoiceOption> Options { get; set; } = new();
    }
}
