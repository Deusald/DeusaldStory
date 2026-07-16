using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    public class StoryProject
    {
        public StoryProjectMetadata                          Metadata          { get; set; } = new();
        public Dictionary<Guid, StoryContainerNode>          ContainerNodes    { get; set; } = new();
        public Dictionary<Guid, StoryLogicNode>              LogicNodes        { get; set; } = new();
        public Dictionary<Guid, StoryPortalNode>             PortalNodes       { get; set; } = new();
        public Dictionary<Guid, StoryImage>                  Images            { get; set; } = new();
        public Dictionary<Guid, StoryVariable>               Variables         { get; set; } = new();

        /// <summary>Reusable node templates. Their definition bodies live out-of-tree in <see cref="ContainerNodes"/>/<see cref="LogicNodes"/>.</summary>
        public Dictionary<Guid, StoryBlueprint>              Blueprints        { get; set; } = new();

        /// <summary>Container-scope blueprint instances (each references a Container/Logic <see cref="StoryBlueprint"/>).</summary>
        public Dictionary<Guid, StoryBlueprintInstanceNode>  BlueprintInstances { get; set; } = new();

        public int GetNumberOfNodes()
        {
            return ContainerNodes.Count + LogicNodes.Count + PortalNodes.Count + BlueprintInstances.Count;
        }
    }
}
