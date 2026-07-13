using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A node that lives only inside a logic node's inner content graph. It references a single story-wide
    /// <see cref="StoryVariable"/>; wiring its <see cref="OutPoint"/> into a SmartFormat node's variables input
    /// supplies that variable's value (under its name) to the format. The variable is referenced by its
    /// <see cref="StoryVariable.Id"/> so a rename of the variable doesn't break the link.
    /// </summary>
    public class StoryExternalVariableNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>The chosen variable's id (a <c>StoryVariable.Id</c>). Empty when nothing picked yet.</summary>
        public Guid SelectedVariableId { get; set; }

        /// <summary>The single output port carrying the variable's value (connects to a SmartFormat variables input).</summary>
        public StoryConnectionPoint OutPoint { get; set; } = new() { Name = "Value" };
    }
}
