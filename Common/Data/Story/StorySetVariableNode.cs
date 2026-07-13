using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// An inner content node on a logic node's flow spine that <b>sets</b> the value of an already-registered
    /// storage variable — the "register unset near the start, assign it later" flow (e.g. mark a place visited when
    /// the player reaches it). It references the owning <see cref="StoryRegisterVariableNode.Id"/> via
    /// <see cref="RegisteredVariableId"/> and carries the new value/assignment. Flow passes straight through
    /// <see cref="FlowIn"/> → <see cref="FlowOut"/>.
    /// </summary>
    public class StorySetVariableNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>The registered variable this sets (a <see cref="StoryRegisterVariableNode.Id"/>). Empty when nothing picked yet.</summary>
        public Guid RegisteredVariableId { get; set; }

        /// <summary>How the value is assigned (Unset clears it back to "not set").</summary>
        public NumberAssignment Assignment { get; set; } = NumberAssignment.SetSpecific;

        /// <summary>The value is set/drawn secretly.</summary>
        public bool Secret { get; set; }

        /// <summary>The value used when <see cref="Assignment"/> is <see cref="NumberAssignment.SetSpecific"/>.</summary>
        public int SpecificValue { get; set; }

        /// <summary>Flow input — wired from a previous flow node's output.</summary>
        public StoryConnectionPoint FlowIn { get; set; } = new() { Name = "Flow" };

        /// <summary>Flow output — wired to the next flow node's input or an Exit.</summary>
        public StoryConnectionPoint FlowOut { get; set; } = new() { Name = "Flow" };
    }
}
