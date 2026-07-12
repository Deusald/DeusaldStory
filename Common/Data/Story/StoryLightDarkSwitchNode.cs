using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A node that lives only inside a logic node's inner content graph. It picks between two icons based on the
    /// render theme: its <see cref="DarkIn"/> and <see cref="LightIn"/> inputs each accept an Icon node's output,
    /// and its <see cref="OutPoint"/> emits an icon (Dark in dark mode, Light in the paper/light preview) that can
    /// be wired wherever an icon is expected — e.g. the Entry node's Icon input.
    /// </summary>
    public class StoryLightDarkSwitchNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>Icon used in dark mode (the default).</summary>
        public StoryConnectionPoint DarkIn { get; set; } = new() { Name = "Dark" };

        /// <summary>Icon used in the paper/light preview.</summary>
        public StoryConnectionPoint LightIn { get; set; } = new() { Name = "Light" };

        /// <summary>The chosen icon, forwarded downstream.</summary>
        public StoryConnectionPoint OutPoint { get; set; } = new() { Name = "Icon" };
    }
}
