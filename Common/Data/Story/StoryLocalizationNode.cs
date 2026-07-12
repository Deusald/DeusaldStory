using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A node that lives only inside a logic node's inner content graph (never in a container). It references a
    /// single localization key from the story's linked localization project; wiring its <see cref="OutPoint"/>
    /// into the Entry node's Title input supplies the logic node's title text. The key is referenced by its
    /// <see cref="LocLocalizationKey.Id"/> so a rename of the key doesn't break the link.
    /// </summary>
    public class StoryLocalizationNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>The chosen localization key's id (a <c>LocLocalizationKey.Id</c>). Empty when nothing picked yet.</summary>
        public Guid SelectedKeyId { get; set; }

        /// <summary>The single output port carrying the resolved text (connects to a Title input).</summary>
        public StoryConnectionPoint OutPoint { get; set; } = new() { Name = "Text" };
    }
}
