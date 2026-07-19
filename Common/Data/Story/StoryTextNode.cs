using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A node that lives only inside a logic node's inner content graph and sits on the flow spine: flow arriving at
    /// its <see cref="FlowIn"/> renders <see cref="Text"/> as one block, then continues out of its
    /// <see cref="FlowOut"/>. Chaining several Text nodes off the Entry is how a logic node renders an ordered
    /// sequence of blocks. The text is authored in place (key + formatting) rather than wired in — with variables
    /// global there is nothing left for a wire to carry.
    /// </summary>
    public class StoryTextNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>Flow input — wired from the Entry's flow output or a previous spine node's flow output.</summary>
        public StoryConnectionPoint FlowIn { get; set; } = new() { Name = "Flow" };

        /// <summary>Flow output — wired to the next spine node's <see cref="FlowIn"/> or the Exit.</summary>
        public StoryConnectionPoint FlowOut { get; set; } = new() { Name = "Flow" };

        /// <summary>The block this node renders — a localization key plus its formatting.</summary>
        public StoryTextConfig Text { get; set; } = new();

        /// <summary>When <c>false</c>, this block is skipped while rendering the interactive <b>App</b> (flow still passes through). Default <c>true</c>.</summary>
        public bool RenderInApp { get; set; } = true;

        /// <summary>When <c>false</c>, this block is skipped while rendering the printed <b>Gamebook</b> (flow still passes through). Default <c>true</c>.</summary>
        public bool RenderInGamebook { get; set; } = true;

        /// <summary>The visual style of the frame this block is drawn in (normal / success / danger / …).</summary>
        public StoryTextFrameStyle FrameStyle { get; set; } = StoryTextFrameStyle.Normal;
    }
}
