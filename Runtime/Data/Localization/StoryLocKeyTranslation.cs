using System;

namespace DeusaldStoryRuntime
{
    /// <summary>
    /// The current translation text for a key+language pair.
    /// TextHash is SHA-256 of the main language text at the time this was last confirmed,
    /// used to detect when the source has drifted and this needs attention.
    /// </summary>
    public class StoryLocKeyTranslation
    {
        /// <summary>BCP-47 language code (e.g. "de-DE").</summary>
        public string LanguageId { get; set; } = string.Empty;

        /// <summary>SHA-256 of the main-language text this translation was based on.</summary>
        public string BaseTextHash { get; set; } = string.Empty;

        public string   Text          { get; set; } = string.Empty;
        public bool     SourceChanged { get; set; }
        public DateTime UpdatedAt     { get; set; } = DateTime.UtcNow;
    }
}