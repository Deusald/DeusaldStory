using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Ambient current UI language for code that produces user-facing text <b>outside</b> the Blazor
    /// component tree — Common services (validator, previews, file IO) and the canvas projection in
    /// <c>EditorModels</c>. Editor components resolve through <c>UiLocalizationService.T</c> instead;
    /// that service keeps <see cref="Current"/> in sync so both paths render the same language.
    ///
    /// English-only until v1.0: <see cref="Current"/> stays <see cref="DefaultLanguage"/> until a UI
    /// language switcher sets it. Lookups fall back to the default language and then to an empty string.
    /// </summary>
    public static class UiLang
    {
        public const string DefaultLanguage = "en-US";

        /// <summary>BCP-47 code the non-component UI currently renders in. Set by UiLocalizationService.</summary>
        public static string Current = DefaultLanguage;

        /// <summary>Resolves <paramref name="keyId"/> in <see cref="Current"/> (SmartFormatted against
        /// <paramref name="values"/>), falling back to the default language then to an empty string.</summary>
        public static string T(Guid keyId, IDictionary<string, object>? values = null)
        {
            string text = Localization.Get(Current, keyId, values);
            if (string.IsNullOrEmpty(text) && Current != DefaultLanguage)
                text = Localization.Get(DefaultLanguage, keyId, values);
            return text;
        }
    }
}
