using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A node that groups other nodes — child containers and logic nodes. The story tree's root is a
    /// container (see <see cref="StoryProjectMetadata.RootStoryContainerNodeId"/>). A container must have at
    /// least one entry point and one exit point; the designer can add as many named ones as they wish and each
    /// is rendered as a non-deletable node inside the container. The root's entry/exit are the story's Start/End.
    /// </summary>
    public class StoryContainerNode : IFileWithId
    {
        public Guid   Id              { get; set; } = Guid.NewGuid();
        public string Name            { get; set; } = string.Empty;
        public string Description     { get; set; } = string.Empty;
        public Guid   ParentContainer { get; set; }
        public double X               { get; set; }
        public double Y               { get; set; }

        public List<StoryConnectionPoint> EntryPoints { get; } = new();
        public List<StoryConnectionPoint> ExitPoints  { get; } = new();

        public List<Guid> Containers { get; } = new();
        public List<Guid> Logic      { get; } = new();
        public List<Guid> Portals    { get; } = new();

        /// <summary>Free-text comment notes placed in this container's graph (no ports; documentation only).</summary>
        public List<StoryCommentNode> Comments { get; } = new();

        /// <summary>Wires between the connection points of this container's own boundary and its children.</summary>
        public List<StoryConnection> Connections { get; } = new();
    }
}
