using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A free-text note the author drops on a graph to explain how things are connected. It has no connection
    /// points and takes no part in playback — it is purely documentation. The same node type is used both in a
    /// container's graph (see <see cref="StoryContainerNode.Comments"/>) and inside a logic node's inner content
    /// graph (see <see cref="StoryLogicNode.CommentNodes"/>); it carries only its canvas position and its text.
    /// </summary>
    public class StoryCommentNode
    {
        public Guid   Id   { get; set; } = Guid.NewGuid();
        public double X    { get; set; }
        public double Y    { get; set; }
        public string Text { get; set; } = string.Empty;
    }
}
