namespace DeusaldStoryCommon
{
    /// <summary>
    /// Which output a logic node is being resolved for. The same inner content graph renders differently for the
    /// interactive <see cref="App"/> and the printed <see cref="Gamebook"/>: App/Gamebook splitter nodes pick their
    /// text/flow branch by this target, so one authored node feeds both mediums. Orthogonal to the light/dark icon
    /// theme (the renderer's <c>paper</c> flag).
    /// </summary>
    public enum StoryRenderTarget
    {
        /// <summary>The interactive app — the player is walked from node to node on screen.</summary>
        App,

        /// <summary>The printed gamebook — the node becomes a numbered section on paper.</summary>
        Gamebook
    }
}
