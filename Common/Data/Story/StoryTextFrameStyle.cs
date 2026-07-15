namespace DeusaldStoryCommon
{
    /// <summary>
    /// The visual style of the frame a rendered text block is drawn in (App preview / Gamebook / PDF export). Chosen
    /// per <see cref="StoryFlowTextNode"/>; <see cref="Normal"/> is the plain, unaccented frame.
    /// </summary>
    public enum StoryTextFrameStyle
    {
        Normal,
        Info,
        Success,
        Warning,
        Danger
    }
}
