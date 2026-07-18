namespace DeusaldStoryCommon
{
    /// <summary>
    /// How a <see cref="StoryLogicNode"/>'s Exit node behaves once a variable is wired into its
    /// <see cref="StoryLogicNode.ExitVariablesIn"/> port. Only meaningful in that "auto" state; ignored otherwise.
    /// </summary>
    public enum StoryExitAutoMode
    {
        /// <summary>
        /// The App auto-picks a single continuation: the first choice whose <see cref="StoryChoice.Condition"/> matches,
        /// else the locked <see cref="StoryChoice.IsElse"/> fallback. The player sees one continue button. Legacy /
        /// default behaviour (value 0, so old projects load as this).
        /// </summary>
        AutomaticChoice,

        /// <summary>
        /// Each choice is shown to the player only when its <see cref="StoryChoice.Condition"/> is empty (always visible)
        /// or evaluates true — the condition gates <b>visibility</b>, not selection. There is no Else fallback, and any
        /// number of choices may be shown. App-only: the printed Gamebook still lists every choice.
        /// </summary>
        ChoiceVisibility
    }
}
