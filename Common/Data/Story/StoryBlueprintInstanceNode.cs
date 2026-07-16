using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A lightweight canvas node that <b>references</b> a Container/Logic <see cref="StoryBlueprint"/> rather than
    /// copying it. Lives on a container's graph (in <see cref="StoryContainerNode.Instances"/>). Its
    /// <see cref="EntryPorts"/>/<see cref="ExitPorts"/> mirror the blueprint definition's boundary points — each
    /// carrying its own connection-point id (what the container's wires reference) mapped to a definition boundary id.
    /// Editing the shared definition changes every instance; at play/preview time the instance is flattened into a
    /// per-instance clone of the definition subtree by <c>StoryBlueprintExpander</c>.
    /// </summary>
    public class StoryBlueprintInstanceNode : IFileWithId
    {
        public Guid   Id              { get; set; } = Guid.NewGuid();
        public Guid   BlueprintId     { get; set; }
        public Guid   ParentContainer { get; set; }
        public double X               { get; set; }
        public double Y               { get; set; }

        public List<StoryBlueprintPortMap> EntryPorts { get; } = new();
        public List<StoryBlueprintPortMap> ExitPorts  { get; } = new();
    }
}
