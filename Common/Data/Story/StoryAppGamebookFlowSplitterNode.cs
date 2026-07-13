using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// An inner content node on a logic node's flow spine that splits the <b>flow</b> by the
    /// <see cref="StoryRenderTarget"/> being rendered: flow arriving at <see cref="FlowIn"/> continues out of
    /// <see cref="AppFlowOut"/> when rendering the App and out of <see cref="GamebookFlowOut"/> when rendering the
    /// printed Gamebook. Either branch may be left unconnected — that medium then simply ends the spine here. Lets one
    /// authored node route the two mediums down different sequences of flow nodes.
    /// </summary>
    public class StoryAppGamebookFlowSplitterNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>Flow input — wired from the Entry's flow output or a previous flow node's output.</summary>
        public StoryConnectionPoint FlowIn { get; set; } = new() { Name = "Flow" };

        /// <summary>Flow output taken when rendering the App — wired to the next flow node or an Exit; optional.</summary>
        public StoryConnectionPoint AppFlowOut { get; set; } = new() { Name = "App" };

        /// <summary>Flow output taken when rendering the Gamebook — wired to the next flow node or an Exit; optional.</summary>
        public StoryConnectionPoint GamebookFlowOut { get; set; } = new() { Name = "Gamebook" };
    }
}
