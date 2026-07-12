using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A directed wire between two connection points inside one container: flow leaves an exit point
    /// (<see cref="FromPoint"/>) and arrives at an entry point (<see cref="ToPoint"/>). The points are
    /// referenced by their <see cref="StoryConnectionPoint.Id"/>, which is unique across the project, so a
    /// connection needs no node ids — the owning container resolves the points back to their nodes.
    /// </summary>
    public class StoryConnection
    {
        public Guid Id        { get; set; } = Guid.NewGuid();
        public Guid FromPoint { get; set; }
        public Guid ToPoint   { get; set; }
    }
}
