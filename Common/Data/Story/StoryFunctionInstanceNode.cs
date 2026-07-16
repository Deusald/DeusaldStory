using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A <see cref="StoryBlueprintKind.Function"/> instance placed <b>inside a logic node's inner content graph</b>
    /// (serialized in <see cref="StoryLogicNode.FunctionInstanceNodes"/>). It references a function
    /// <see cref="StoryBlueprint"/> and is wired into the LFlow spine via <see cref="FlowIn"/>/<see cref="FlowOut"/>,
    /// plus one typed port per signature input/output. At play/preview time the function's inner graph is inlined in
    /// place by <c>StoryBlueprintExpander</c>.
    /// </summary>
    public class StoryFunctionInstanceNode : IFileWithId
    {
        public Guid   Id          { get; set; } = Guid.NewGuid();
        public Guid   BlueprintId { get; set; }
        public double X           { get; set; }
        public double Y           { get; set; }

        /// <summary>This instance's own LFlow spine-in port (binds to the definition's Entry LFlow-out).</summary>
        public StoryConnectionPoint FlowIn { get; set; } = new() { Name = "Flow" };

        /// <summary>This instance's own LFlow spine-out port (binds to the definition's Exit LFlow-in).</summary>
        public StoryConnectionPoint FlowOut { get; set; } = new() { Name = "Flow" };

        /// <summary>One port per function signature <b>input</b>: own id -&gt; signature input id.</summary>
        public List<StoryBlueprintPortMap> InputPorts { get; } = new();

        /// <summary>One port per function signature <b>output</b>: own id -&gt; signature output id.</summary>
        public List<StoryBlueprintPortMap> OutputPorts { get; } = new();
    }
}
