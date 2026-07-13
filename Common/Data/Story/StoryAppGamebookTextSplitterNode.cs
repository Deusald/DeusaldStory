using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A node that lives only inside a logic node's inner content graph and emits one of two texts depending on the
    /// <see cref="StoryRenderTarget"/> being rendered: its <see cref="AppTextIn"/> input is used for the App and its
    /// <see cref="GamebookTextIn"/> input for the printed Gamebook. Either input may be left unconnected — that medium
    /// then simply renders empty text for this path. Its <see cref="OutPoint"/> emits the selected text, wired wherever
    /// a text is expected (the Entry's Title/Subtitle, a FlowText, a Choice option, or another text node's input).
    /// </summary>
    public class StoryAppGamebookTextSplitterNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>The text used when rendering the App — accepts a Localization/SmartFormat (or splitter) text output; optional.</summary>
        public StoryConnectionPoint AppTextIn { get; set; } = new() { Name = "App Text" };

        /// <summary>The text used when rendering the Gamebook — accepts a Localization/SmartFormat (or splitter) text output; optional.</summary>
        public StoryConnectionPoint GamebookTextIn { get; set; } = new() { Name = "Gamebook Text" };

        /// <summary>The single output port carrying the medium-selected text (connects to a text input).</summary>
        public StoryConnectionPoint OutPoint { get; set; } = new() { Name = "Text" };
    }
}
