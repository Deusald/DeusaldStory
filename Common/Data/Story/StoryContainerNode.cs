using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    public class StoryContainerNode : IFileWithId
    {
        public Guid   Id              { get; set; } = Guid.NewGuid();
        public string Name            { get; set; } = string.Empty;
        public Guid   ParentContainer { get; set; }

        public List<Guid> Containers { get; } = new();
        public List<Guid> Logic      { get; } = new();
    }
}