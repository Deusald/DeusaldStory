namespace DeusaldStoryCommon
{
    /// <summary>
    /// How a <see cref="StorySetExternalVariableNode"/> decides the value it assigns to its external variable.
    /// </summary>
    public enum StorySetExternalVariableMode
    {
        /// <summary>Assigns a fixed value picked from the variable's possible values (the default).</summary>
        SpecificValue,

        /// <summary>
        /// Assigns whatever value arrives on the node's variable input — the value is mapped from a wired variable
        /// source (an External Variable or Prev Exit Variable output) at play time instead of being fixed.
        /// </summary>
        MapFromVariable,

        /// <summary>
        /// Like <see cref="MapFromVariable"/>, the assigned value comes from the node's wired variable input, but each
        /// incoming value is first translated through the node's <c>ValueMap</c> conversion table before being assigned
        /// — letting the author define which incoming value becomes which external-variable value.
        /// </summary>
        RemapFromVariable
    }
}
