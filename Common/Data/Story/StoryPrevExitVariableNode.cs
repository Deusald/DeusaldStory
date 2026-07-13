using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// One remap row on a <see cref="StoryPrevExitVariableNode"/>: the incoming exit named by
    /// <see cref="SourceExitId"/> (a <c>StoryConnectionPoint.Id</c> on the upstream logic node's Exit points) is
    /// emitted as <see cref="Value"/> instead of its raw name. Keyed by id so an upstream exit rename doesn't break
    /// the mapping.
    /// </summary>
    public class StoryPrevExitRemap
    {
        public Guid   SourceExitId { get; set; }
        public string Value        { get; set; } = string.Empty;
    }

    /// <summary>
    /// The non-deletable node that appears inside a logic node's inner graph when the node has
    /// <see cref="StoryLogicNode.AcceptExitVariable"/> on and its <see cref="StoryLogicNode.ExitVariableIn"/> is
    /// wired to an upstream node's <b>Selection</b> variable. It exposes that variable's value (the name of the
    /// exit the upstream node took) on <see cref="OutPoint"/>, ready to feed a SmartFormat or any other variable
    /// input. The author can <see cref="VariableName">rename</see> it locally and, with <see cref="RemapEnabled"/>,
    /// <see cref="Remaps">remap</see> each incoming exit name to a custom output value. There is exactly one per
    /// logic node (always present, like the Entry); it is only drawn when relevant.
    /// </summary>
    public class StoryPrevExitVariableNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>The local name this variable is exposed under inside this logic node (defaults to "Selection").</summary>
        public string VariableName { get; set; } = "Selection";

        /// <summary>When set, incoming exit names are translated through <see cref="Remaps"/> before being emitted.</summary>
        public bool RemapEnabled { get; set; }

        /// <summary>Per-exit output-value overrides (keyed by the upstream exit point id); an unmapped exit emits its name.</summary>
        public List<StoryPrevExitRemap> Remaps { get; set; } = new();

        /// <summary>The single output port carrying the (optionally remapped) value — connects to a variable input.</summary>
        public StoryConnectionPoint OutPoint { get; set; } = new() { Name = "Value" };
    }
}
