using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A story-wide variable the gamebook can read and branch on. It has a unique <see cref="Name"/> (also the
    /// placeholder token used inside condition localization text, e.g. <c>{Health}</c>), an optional
    /// <see cref="Description"/>, and the list of <see cref="PossibleValues"/> the designer expects it to take.
    /// <see cref="ConditionKeyId"/> optionally links a "gamebook condition" localization key whose SmartFormat text
    /// reads off this variable, so the editor can preview how that text renders for a chosen value. Stored one
    /// <c>Variables/{guid}.json</c> file per variable.
    /// </summary>
    public class StoryVariable : IFileWithId
    {
        public Guid         Id             { get; set; } = Guid.NewGuid();
        public string       Name           { get; set; } = string.Empty;
        public string       Description    { get; set; } = string.Empty;
        public List<string> PossibleValues { get; set; } = new();

        /// <summary>
        /// When set, this variable is a <b>constant</b> — its value is fixed before any Gamebook section is evaluated
        /// (e.g. the printed language). An External Variable node referencing it then exposes a <c>CVariable</c> port
        /// and it may be used in Gamebook text; a non-constant variable is App-only and never reaches the Gamebook.
        /// Constants never dimension sections (one value is chosen per render).
        /// </summary>
        public bool IsConstant { get; set; }

        /// <summary>The linked gamebook-condition localization key id (a <c>LocLocalizationKey.Id</c>). Empty = none.</summary>
        public Guid ConditionKeyId { get; set; }
    }
}
