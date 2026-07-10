using System;
using System.Collections.Generic;

namespace DeusaldStoryRuntime
{
    public class StoryLocLocalizationKey : IFileWithId
    {
        public Guid     Id          { get; set; } = Guid.NewGuid();
        public Guid     CategoryId  { get; set; }
        public string   KeyName     { get; set; } = string.Empty;
        public string   Description { get; set; } = string.Empty;
        public DateTime CreatedAt   { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt   { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Maximum character length for all translations of this key.
        /// 0 means no limit enforced.
        /// </summary>
        public int MaxLength { get; set; }

        /// <summary>Free-form tags for search/filter (e.g. ["ui", "button"]).</summary>
        public List<string> Tags { get; set; } = new();

        /// <summary>All translations for this key across every language.</summary>
        public List<StoryLocKeyTranslation> Translations { get; set; } = new();
    }
}