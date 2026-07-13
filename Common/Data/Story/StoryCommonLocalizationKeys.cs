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

        // ── Storage-variable player instructions (SmartFormat tokens: {slot}, {max}, {value}) ──

        /// <summary>Release a storage slot — e.g. "Clear slot NA and return its component."</summary>
        public static readonly Guid StorageClearSlot = new("a1b2c3d4-0010-4000-8000-000000000010");

        /// <summary>Number/D6, full range — "Roll a D6 and place it on slot {slot}."</summary>
        public static readonly Guid StorageNumberDiceFull = new("a1b2c3d4-0011-4000-8000-000000000011");

        /// <summary>Number/D6, reduced range — "Roll a D6, rerolling until 1–{max}, and place it on slot {slot}."</summary>
        public static readonly Guid StorageNumberDiceReroll = new("a1b2c3d4-0012-4000-8000-000000000012");

        /// <summary>Number/token, random draw.</summary>
        public static readonly Guid StorageNumberTokenRandom = new("a1b2c3d4-0013-4000-8000-000000000013");

        /// <summary>Number/token, secret random draw.</summary>
        public static readonly Guid StorageNumberTokenRandomSecret = new("a1b2c3d4-0014-4000-8000-000000000014");

        /// <summary>Number/token, specific value.</summary>
        public static readonly Guid StorageNumberTokenSpecific = new("a1b2c3d4-0015-4000-8000-000000000015");

        /// <summary>Number/token, secret specific value.</summary>
        public static readonly Guid StorageNumberTokenSpecificSecret = new("a1b2c3d4-0016-4000-8000-000000000016");

        /// <summary>Number, one-value presence flag — mark the slot as set.</summary>
        public static readonly Guid StorageNumberFlagSet = new("a1b2c3d4-0017-4000-8000-000000000017");

        /// <summary>Dial, specific value.</summary>
        public static readonly Guid StorageDialSet = new("a1b2c3d4-0018-4000-8000-000000000018");

        /// <summary>Dial, secret specific value.</summary>
        public static readonly Guid StorageDialSetSecret = new("a1b2c3d4-0019-4000-8000-000000000019");

        /// <summary>Dial, random value.</summary>
        public static readonly Guid StorageDialRandom = new("a1b2c3d4-001a-4000-8000-00000000001a");

        /// <summary>String, write a value on the sheet.</summary>
        public static readonly Guid StorageStringWrite = new("a1b2c3d4-001b-4000-8000-00000000001b");

        // Built-in English fallbacks, keyed by the ids above, used when the linked localization lacks the key.
        private static readonly Dictionary<Guid, string> _Fallbacks = new()
        {
            [AppContinueButton]                = "Click here to continue…",
            [GamebookContinueToSection]        = "To continue go to section {section}",
            [GamebookContinueConditional]      = "To continue {condition} go to section {section}",
            [GamebookTheEnd]                   = "The End",
            [StorageClearSlot]                 = "Clear slot {slot} and return its component.",
            [StorageNumberDiceFull]            = "Roll a D6 and place it on slot {slot}.",
            [StorageNumberDiceReroll]          = "Roll a D6, rerolling until it shows 1–{max}, and place it on slot {slot}.",
            [StorageNumberTokenRandom]         = "Draw a random token numbered 1–{max}, place it on slot {slot}, and return the rest to the bag.",
            [StorageNumberTokenRandomSecret]   = "Secretly draw a random token numbered 1–{max}, place it face-down on slot {slot}, and return the rest to the bag.",
            [StorageNumberTokenSpecific]       = "Place token {value} on slot {slot}.",
            [StorageNumberTokenSpecificSecret] = "Secretly place token {value} face-down on slot {slot}.",
            [StorageNumberFlagSet]             = "Place a token on slot {slot} to mark it set.",
            [StorageDialSet]                   = "Set dial {slot} to {value}.",
            [StorageDialSetSecret]             = "Secretly set dial {slot} to {value} (keep the hidden side up).",
            [StorageDialRandom]                = "Set dial {slot} to a random value from −7 to 7.",
            [StorageStringWrite]               = "Write the required value on sheet slot {slot}."
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
