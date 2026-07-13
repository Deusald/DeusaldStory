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
        /// <summary>App preview's dummy continue button — e.g. "Click here to continue…".</summary>
        public static readonly Guid AppContinueButton = new("a1b2c3d4-0001-4000-8000-000000000001");

        /// <summary>Gamebook unconditional continue line. SmartFormat token: <c>{section}</c>.</summary>
        public static readonly Guid GamebookContinueToSection = new("a1b2c3d4-0002-4000-8000-000000000002");

        /// <summary>Gamebook conditional continue line. SmartFormat tokens: <c>{condition}</c>, <c>{section}</c>.</summary>
        public static readonly Guid GamebookContinueConditional = new("a1b2c3d4-0003-4000-8000-000000000003");

        /// <summary>Gamebook end-of-story line — e.g. "The End".</summary>
        public static readonly Guid GamebookTheEnd = new("a1b2c3d4-0004-4000-8000-000000000004");

        // Built-in English fallbacks, keyed by the ids above, used when the linked localization lacks the key.
        private static readonly Dictionary<Guid, string> _Fallbacks = new()
        {
            [AppContinueButton]           = "Click here to continue…",
            [GamebookContinueToSection]   = "To continue go to section {section}",
            [GamebookContinueConditional] = "To continue {condition} go to section {section}",
            [GamebookTheEnd]              = "The End"
        };

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

        /// <summary>The main-language text for <paramref name="keyId"/>, falling back to the built-in English default.</summary>
        private static string TemplateFor(LocProject? localization, Guid keyId)
        {
            LocLocalizationKey? key  = localization?.Keys.Find(k => k.Id == keyId);
            string?             main = localization?.Metadata.MainLanguageId;

            if (key is not null && !string.IsNullOrEmpty(main)
                && key.Translations.Find(t => t.LanguageId == main)?.Text is { Length: > 0 } text)
                return text;

            return _Fallbacks.TryGetValue(keyId, out string? fallback) ? fallback : string.Empty;
        }
    }
}
