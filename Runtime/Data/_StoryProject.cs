using System;
using System.Collections.Generic;

namespace DeusaldStoryRuntime
{
    public class StoryProject
    {
        public StoryProjectMetadata                 Metadata       { get; set; } = new();
        public List<StoryLocCategory>               LocCategories  { get; set; } = new();
        public List<StoryLocLocalizationKey>        LocKeys        { get; set; } = new();
        public Dictionary<Guid, StoryContainerNode> ContainerNodes { get; set; } = new();
        public Dictionary<Guid, StoryLogicNode>     LogicNodes     { get; set; } = new();

        public int GetNumberOfNodes()
        {
            return ContainerNodes.Count + LogicNodes.Count;
        }
    }
}