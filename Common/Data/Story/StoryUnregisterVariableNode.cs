using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// An inner content node on a logic node's flow spine that <b>unregisters</b> (releases) a registered storage
    /// variable, freeing its physical slot for reuse and instructing the player to clear that slot. It references
    /// the owning <see cref="StoryRegisterVariableNode.Id"/> via <see cref="RegisteredVariableId"/>. Flow passes
    /// straight through <see cref="FlowIn"/> → <see cref="FlowOut"/>.
    /// </summary>
    public class StoryUnregisterVariableNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>The registered variable this releases (a <see cref="StoryRegisterVariableNode.Id"/>). Empty when nothing picked yet.</summary>
        public Guid RegisteredVariableId { get; set; }

        /// <summary>Flow input — wired from a previous flow node's output.</summary>
        public StoryConnectionPoint FlowIn { get; set; } = new() { Name = "Flow" };

        /// <summary>Flow output — wired to the next flow node's input or an Exit.</summary>
        public StoryConnectionPoint FlowOut { get; set; } = new() { Name = "Flow" };
    }
}
