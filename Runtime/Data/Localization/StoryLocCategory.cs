using System;

namespace DeusaldStoryRuntime
{
    /// <summary>
    /// Organises keys into a hierarchy. ParentCategoryId = null means root category.
    /// </summary>
    public class StoryLocCategory
    {
        public Guid   Id               { get; set; } = Guid.NewGuid();
        public Guid?  ParentCategoryId { get; set; }
        public string Name             { get; set; } = string.Empty;
        public string Description      { get; set; } = string.Empty;
    }
}