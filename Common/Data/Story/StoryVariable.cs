using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A global, catalogued story variable. Every variable lives in <see cref="StoryProject.Variables"/> (one
    /// <c>Variables/{guid}.json</c> file each) and has a unique <see cref="Name"/> — which doubles as the SmartFormat
    /// placeholder token used in text (e.g. <c>{Health}</c>) — and an optional <see cref="Description"/>.
    ///
    /// A variable is either <see cref="StoryVariableScope.External"/> (tracked by the game's own components) or
    /// <see cref="StoryVariableScope.Internal"/> (stored on the scenario's board slots / dials / paper table). The
    /// <see cref="Scope"/> selects which block of fields below is meaningful; the rest are ignored.
    /// </summary>
    public class StoryVariable : IFileWithId
    {
        public Guid   Id          { get; set; } = Guid.NewGuid();
        public string Name        { get; set; } = string.Empty;
        public string Description  { get; set; } = string.Empty;

        /// <summary>External (game-component tracked) vs Internal (scenario-storage backed).</summary>
        public StoryVariableScope Scope { get; set; } = StoryVariableScope.External;

        /// <summary>True for the built-in Medium / Theme variables — surfaced but not editable or deletable.</summary>
        public bool IsReadOnly { get; set; }

        // ---- External ----

        /// <summary>How much is known up front (Runtime / Initial / Constant). External only.</summary>
        public StoryExternalForm ExternalForm { get; set; } = StoryExternalForm.Runtime;

        /// <summary>The value domain (Bool / Number / Text). External only.</summary>
        public StoryExternalSubtype ExternalSubtype { get; set; } = StoryExternalSubtype.Bool;

        /// <summary>Inclusive lower bound for a Number subtype.</summary>
        public int NumberFrom { get; set; }

        /// <summary>Inclusive upper bound for a Number subtype.</summary>
        public int NumberTo { get; set; } = 10;

        /// <summary>Free-form vs fixed Options, for a Text subtype.</summary>
        public StoryTextForm TextForm { get; set; } = StoryTextForm.Free;

        /// <summary>Minimum character count for a Free Text subtype (1..30).</summary>
        public int TextMinLength { get; set; } = 1;

        /// <summary>Maximum character count for a Free Text subtype (1..30).</summary>
        public int TextMaxLength { get; set; } = 30;

        /// <summary>The allowed values for a Text/Options subtype (enum-like).</summary>
        public List<string> TextOptions { get; set; } = new();

        /// <summary>The known value for an Initial or Constant External variable (empty for Runtime).</summary>
        public string FixedValue { get; set; } = string.Empty;

        // ---- Internal ----

        /// <summary>Which storage component holds the value (Small Number / Big Public / Big Secret / Text). Internal only.</summary>
        public StoryInternalSubtype InternalSubtype { get; set; } = StoryInternalSubtype.SmallNumber;

        /// <summary>Die vs Token (Token = secret) for a Small Number variable.</summary>
        public SmallNumberSource SmallNumberSource { get; set; } = SmallNumberSource.Dice;

        /// <summary>How a Small Number's 1–6 face folds into logical buckets.</summary>
        public SmallNumberValuesMap ValuesMap { get; set; } = SmallNumberValuesMap.SixValues;

        /// <summary>Named bucket→string translations for a Small Number variable.</summary>
        public List<StoryVariableTextMap> TextMaps { get; set; } = new();

        /// <summary>The index of the slot this variable occupies within its subtype's bank (0 ⇒ A).</summary>
        public int SlotIndex { get; set; }

        /// <summary>How long the slot is reserved (Scenario = exclusive for the whole story, Chapter = per container).</summary>
        public StoryVariableLifespan Lifespan { get; set; } = StoryVariableLifespan.Scenario;

        // ---- Choice labelling ----

        /// <summary>
        /// The localization key labelling a <see cref="StoryVariableChoiceMode.ValueBased"/> choice on this variable —
        /// the button / "go to section" phrase printed once per possible value. SmartFormatted against the logic
        /// node's variable dictionary, so it may reference this variable's own token or <c>{ChoiceA}</c>.
        /// </summary>
        public Guid ValueConditionKeyId { get; set; }

        /// <summary>
        /// The localization key labelling an option-based choice that reads this variable
        /// (see <see cref="StoryChoiceOption"/>). Overridden by an option's own key when it sets one.
        /// </summary>
        public Guid OptionConditionKeyId { get; set; }
    }
}
