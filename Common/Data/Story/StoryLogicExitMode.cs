namespace DeusaldStoryCommon
{
    /// <summary>
    /// How a <see cref="StoryLogicNode"/> exposes its exit branches on its card. The node always keeps its named
    /// exit points internally; this only changes the ports drawn on the outer graph and how flow leaves the node.
    /// </summary>
    public enum StoryLogicExitMode
    {
        /// <summary>Each exit point is its own output port — a separate flow the story can be wired to (the default).</summary>
        SeparatePaths,

        /// <summary>
        /// All exit points collapse into a single flow output plus a <b>Selection</b> variable output that carries the
        /// name of whichever exit the flow reached, so a later node can read and branch/format on it.
        /// </summary>
        SingleSelection
    }
}
