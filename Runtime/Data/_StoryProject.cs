using System.Collections.Generic;

namespace DeusaldStoryRuntime
{
    public class StoryProject
    {
        public StoryProjectMetadata          Metadata               { get; set; } = new();
        public List<StoryLocCategory>        LocalizationCategories { get; set; } = new();
        public List<StoryLocLocalizationKey> LocalizationKeys       { get; set; } = new();
    }
}