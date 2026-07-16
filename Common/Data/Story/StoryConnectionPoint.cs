using System;

namespace DeusaldStoryCommon
{
    /// <summary>Whether a container boundary point carries a plain flow or a variable-carrying flow.</summary>
    public enum StoryPointFlow
    {
        Flow,
        VFlow
    }

    /// <summary>
    /// A named connection point on a node — an entry (flow arrives here) or an exit (flow leaves here).
    /// On a container these are rendered as their own non-deletable nodes inside the container, so they carry
    /// a canvas position; on a logic node they are ports on the card and <see cref="X"/>/<see cref="Y"/> are unused.
    /// </summary>
    public class StoryConnectionPoint
    {
        public Guid   Id   { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public double X    { get; set; }
        public double Y    { get; set; }

        /// <summary>
        /// For a container boundary point, whether it is a plain <see cref="StoryPointFlow.Flow"/> or a
        /// variable-carrying <see cref="StoryPointFlow.VFlow"/> port. Unused on logic-node inner points.
        /// Defaults to <see cref="StoryPointFlow.Flow"/> so pre-existing projects load unchanged.
        /// </summary>
        public StoryPointFlow FlowKind { get; set; } = StoryPointFlow.Flow;
    }
}
