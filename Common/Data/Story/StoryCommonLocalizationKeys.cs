using System;
using System.Collections.Generic;
using DeusaldLocalizerCommon;
using JetBrains.Annotations;
using SmartFormat;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// The fixed set of "common" localization keys every story relies on for framework-generated text — the App's
    /// continue button and the printed Gamebook's continue-instructions. Each field's value is the localization
    /// <b>key id</b> (a <see cref="LocLocalizationKey.Id"/>), matching how <see cref="StoryLocalizationNode.SelectedKeyId"/>
    /// references keys — not the human key name. They are <c>static readonly Guid</c> because a <see cref="Guid"/>
    /// cannot be a compile-time <c>const</c>.
    ///
    /// TODO(v1.0.0): these keys currently must be authored into the story's linked Localization project under these
    /// exact ids; once the app ships with embedded compiled localization, resolve them from there instead of the
    /// linked project (see <see cref="Resolve"/>). Until a key is present, <see cref="Resolve"/> falls back to a
    /// built-in English default so previews still read.
    /// </summary>
    [PublicAPI]
    public static class StoryCommonLocalizationKeys
    {
        // These framework keys now live in the editor localization catalog (Localization.Story) so the
        // Localization tool owns them at v1.0. The ids here alias the catalog's (stable) ids and the English
        // fallback text is read back from the catalog (see TemplateFor). Story data referencing these Guids
        // keeps resolving unchanged — the alias is the exact same id.
        public static readonly Guid AppContinueButton                = Localization.Story.AppContinueButton;
        public static readonly Guid GamebookContinueToSection        = Localization.Story.GamebookContinueToSection;
        public static readonly Guid GamebookContinueConditional      = Localization.Story.GamebookContinueConditional;
        public static readonly Guid GamebookTheEnd                   = Localization.Story.GamebookTheEnd;
        public static readonly Guid GamebookChoiceToSection          = Localization.Story.GamebookChoiceToSection;
        public static readonly Guid GamebookGatherHubCard            = Localization.Story.GamebookGatherHubCard;
        public static readonly Guid GamebookGatherHubCardChoice      = Localization.Story.GamebookGatherHubCardChoice;
        public static readonly Guid GamebookGoToSection              = Localization.Story.GamebookGoToSection;
        public static readonly Guid StorageClearSlot                 = Localization.Story.StorageClearSlot;
        public static readonly Guid StorageNumberDiceFull            = Localization.Story.StorageNumberDiceFull;
        public static readonly Guid StorageNumberDiceReroll          = Localization.Story.StorageNumberDiceReroll;
        public static readonly Guid StorageNumberTokenRandom         = Localization.Story.StorageNumberTokenRandom;
        public static readonly Guid StorageNumberTokenRandomSecret   = Localization.Story.StorageNumberTokenRandomSecret;
        public static readonly Guid StorageNumberTokenSpecific       = Localization.Story.StorageNumberTokenSpecific;
        public static readonly Guid StorageNumberTokenSpecificSecret = Localization.Story.StorageNumberTokenSpecificSecret;
        public static readonly Guid StorageNumberFlagSet             = Localization.Story.StorageNumberFlagSet;
        public static readonly Guid StorageDialSet                   = Localization.Story.StorageDialSet;
        public static readonly Guid StorageDialSetSecret             = Localization.Story.StorageDialSetSecret;
        public static readonly Guid StorageDialRandom                = Localization.Story.StorageDialRandom;
        public static readonly Guid StorageStringWrite               = Localization.Story.StorageStringWrite;
        public static readonly Guid StorageNumberDiceSpecific        = Localization.Story.StorageNumberDiceSpecific;
        public static readonly Guid StorageStringWriteSpecific       = Localization.Story.StorageStringWriteSpecific;
        public static readonly Guid RandomRollD12                    = Localization.Story.RandomRollD12;
        public static readonly Guid RandomRollBand                   = Localization.Story.RandomRollBand;

        /// <summary>
        /// Resolves one of the common keys to display text: the main-language translation of the key in
        /// <paramref name="localization"/> (or a built-in English fallback when unavailable), SmartFormatted against
        /// <paramref name="values"/>. On a formatting error the raw template is returned.
        /// </summary>
        public static string Resolve(LocProject? localization, Guid keyId, IDictionary<string, object>? values = null)
        {
            string template = TemplateFor(localization, keyId);
            if (values is null || values.Count == 0) return template;

            return StoryConditionPreview.Render(template, values, out _);
        }

        /// <summary>The main-language text for <paramref name="keyId"/>, falling back to the built-in English
        /// default held in the <see cref="Localization"/> catalog (<c>en-US</c>).</summary>
        private static string TemplateFor(LocProject? localization, Guid keyId)
        {
            LocLocalizationKey? key  = localization?.Keys.Find(k => k.Id == keyId);
            string?             main = localization?.Metadata.MainLanguageId;

            if (key is not null && !string.IsNullOrEmpty(main)
                && key.Translations.Find(t => t.LanguageId == main)?.Text is { Length: > 0 } text)
                return text;

            return Localization.Get(UiLang.DefaultLanguage, keyId);
        }
    }
}
