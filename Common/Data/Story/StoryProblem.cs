using System;

namespace DeusaldStoryCommon
{
    /// <summary>How serious a validation problem is.</summary>
    public enum StoryProblemSeverity
    {
        Error,
        Warning
    }

    /// <summary>
    /// One issue found by <see cref="DeusaldStoryCommon.StoryGraphValidator"/>. Besides the human-readable
    /// <see cref="Message"/> it carries the graph location so the editor can navigate to it: the
    /// <see cref="ContainerId"/> to open, the <see cref="LogicNodeId"/> to drill into (if the problem is inside a
    /// logic node's inner graph), the <see cref="InnerNodeId"/> to select there, and the <see cref="PointId"/> of a
    /// dangling connector.
    /// </summary>
    public sealed class StoryProblem
    {
        public StoryProblemSeverity Severity    { get; set; }
        public string               Message     { get; set; } = string.Empty;
        public Guid                 ContainerId { get; set; }
        public Guid                 LogicNodeId { get; set; }
        public Guid                 InnerNodeId { get; set; }
        public Guid                 PointId     { get; set; }
    }
}
