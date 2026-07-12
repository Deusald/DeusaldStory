using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// The heart of the system — a stop point in the story that generates content and runs calculations on
    /// variables. The story is played by moving from one logic node to the next. A logic node has exactly one
    /// entry point and at least one exit point (a branch the story can take from here).
    /// </summary>
    public class StoryLogicNode : IFileWithId
    {
        public Guid   Id              { get; set; } = Guid.NewGuid();
        public string Name            { get; set; } = string.Empty;
        public string Description     { get; set; } = string.Empty;
        public Guid   ParentContainer { get; set; }
        public double X               { get; set; }
        public double Y               { get; set; }

        public StoryConnectionPoint       EntryPoint { get; set; } = new() { Name = "In" };
        public List<StoryConnectionPoint> ExitPoints { get; }      = new();
    }
}
