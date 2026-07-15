namespace DeusaldStoryCommon
{
    /// <summary>
    /// A single conversion row used by a <see cref="StorySetExternalVariableNode"/> in
    /// <see cref="StorySetExternalVariableMode.RemapFromVariable"/> mode: when the value wired into the node equals
    /// <see cref="From"/>, the external variable is assigned <see cref="To"/> (one of its declared possible values)
    /// instead of the raw incoming value.
    /// </summary>
    public class StorySetExternalVariableRemap
    {
        /// <summary>The incoming value (from the wired variable source) this row matches.</summary>
        public string From { get; set; } = string.Empty;

        /// <summary>The external variable value assigned when the incoming value matches <see cref="From"/>.</summary>
        public string To { get; set; } = string.Empty;
    }
}
