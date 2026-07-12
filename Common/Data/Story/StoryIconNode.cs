using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A node that lives only inside a logic node's inner content graph (never in a container). It references a
    /// single icon from the project's image library (a <see cref="StoryImage"/> with
    /// <see cref="StoryImageKind.Icon"/>); wiring its <see cref="OutPoint"/> into the Entry node's Icon input
    /// supplies the logic node's icon. The image is referenced by its <see cref="StoryImage.Id"/>.
    /// </summary>
    public class StoryIconNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>The chosen icon's id (a <c>StoryImage.Id</c>, Kind = Icon). Empty when nothing picked yet.</summary>
        public Guid SelectedImageId { get; set; }

        /// <summary>The single output port carrying the icon (connects to an Icon input).</summary>
        public StoryConnectionPoint OutPoint { get; set; } = new() { Name = "Icon" };
    }
}
