using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    public class StoryProject
    {
        public StoryProjectMetadata                 Metadata       { get; set; } = new();
        public Dictionary<Guid, StoryContainerNode> ContainerNodes { get; set; } = new();
        public Dictionary<Guid, StoryLogicNode>     LogicNodes     { get; set; } = new();
        public Dictionary<Guid, StoryPortalNode>    PortalNodes    { get; set; } = new();
        public Dictionary<Guid, StoryImage>         Images         { get; set; } = new();
        public Dictionary<Guid, StoryVariable>      Variables      { get; set; } = new();

        public int GetNumberOfNodes()
        {
            return ContainerNodes.Count + LogicNodes.Count + PortalNodes.Count;
        }
    }
}
