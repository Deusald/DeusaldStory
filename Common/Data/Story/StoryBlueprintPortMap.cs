using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// One boundary port of a blueprint instance. <see cref="Id"/> is this instance's <b>own</b> connection-point id —
    /// the id that the surrounding graph's wires actually reference, unique per instance so two instances of the same
    /// blueprint wire independently. <see cref="DefinitionPointId"/> is a <i>reference</i> to the matching boundary
    /// point inside the shared definition (an entry/exit point id, or a Function signature port id), left untouched by
    /// id-remapping clone so it keeps resolving after copy/paste.
    /// </summary>
    public class StoryBlueprintPortMap
    {
        public Guid   Id                { get; set; } = Guid.NewGuid();
        public Guid   DefinitionPointId { get; set; }
        public string Name              { get; set; } = string.Empty;
    }
}
