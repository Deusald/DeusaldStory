using System;

namespace DeusaldStoryRuntime
{
    public class StoryLogicNode : IFileWithId
    {
        public Guid   Id              { get; set; } = Guid.NewGuid();
        public string Name            { get; set; } = string.Empty;
        public Guid   ParentContainer { get; set; }
    }
}