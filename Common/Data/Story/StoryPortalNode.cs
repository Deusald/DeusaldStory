using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A portal "pair" (orange) that lives inside a container to keep the graph clean: flow that arrives at any
    /// of its <see cref="InPoints"/> teleports to the single <see cref="OutPoint"/> and continues from whatever
    /// the out point is wired to. Creating a portal always spawns two canvas nodes — a <b>portal in</b> and a
    /// <b>portal out</b> — and the designer can add more portal ins to the same pair later. Playback resolves a
    /// portal by propagating from every portal in to the portal out, then on to the out point's connection.
    /// </summary>
    public class StoryPortalNode : IFileWithId
    {
        public Guid   Id              { get; set; } = Guid.NewGuid();
        public string Name            { get; set; } = string.Empty;
        public string Description     { get; set; } = string.Empty;
        public Guid   ParentContainer { get; set; }

        /// <summary>
        /// The single "portal out" node — flow re-emerges here. Rendered as a node with one output port, so it
        /// carries a canvas <see cref="StoryConnectionPoint.X"/>/<see cref="StoryConnectionPoint.Y"/>.
        /// </summary>
        public StoryConnectionPoint OutPoint { get; set; } = new() { Name = "Out" };

        /// <summary>
        /// The "portal in" nodes (at least one) — flow arriving at any of these teleports to <see cref="OutPoint"/>.
        /// Each is rendered as a node with one input port and carries its own canvas position.
        /// </summary>
        public List<StoryConnectionPoint> InPoints { get; } = new();
    }
}
