namespace DeusaldStoryCommon
{
    /// <summary>
    /// How a <see cref="StoryLogicNode"/> hands its continuations to the next node(s). The node always owns its list
    /// of <see cref="StoryLogicNode.Choices"/>; this changes the outer ports drawn on its card and how flow leaves it.
    /// </summary>
    public enum StoryLogicExitMode
    {
        /// <summary>Each choice is its own outer <c>Flow</c> output — a separate path wired to a (possibly different) next node.</summary>
        ManyPaths,

        /// <summary>
        /// All choices share one outer <c>VFlow</c> output that carries the node's declared variables
        /// (<see cref="StoryLogicNode.DeclaredVariables"/>); each choice pins the variables' values. The consuming node
        /// reads them via its Prev Exit Variable node, and the Gamebook expands into one section per choice.
        /// </summary>
        SinglePath
    }
}
