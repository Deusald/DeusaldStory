using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A node that lives only inside a logic node's inner content graph and sits on the <b>flow</b> spine. It affects the
    /// interactive <b>App</b> only: everything rendered up to this point becomes one screen ending in a
    /// "Click here to continue…" button; pressing it keeps the same title / subtitle / icon but restarts the content
    /// area from whatever follows this node (up to the next split, or the exit choices on the last page). The printed
    /// <b>Gamebook</b> ignores it entirely — flow just passes through, so the section reads as one continuous block.
    /// </summary>
    public class StorySplitForAppNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>Flow input — wired from the Entry's flow output or a previous flow node's <see cref="FlowOut"/>.</summary>
        public StoryConnectionPoint FlowIn { get; set; } = new() { Name = "Flow" };

        /// <summary>Flow output — the content that continues on the next App page (or the same Gamebook section).</summary>
        public StoryConnectionPoint FlowOut { get; set; } = new() { Name = "Flow" };
    }
}
