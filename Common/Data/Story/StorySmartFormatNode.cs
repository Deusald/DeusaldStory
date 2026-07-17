using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A node that lives only inside a logic node's inner content graph. It formats a localization text with
    /// <see href="https://github.com/axuno/SmartFormat">SmartFormat</see>: its <see cref="LocalizationIn"/> input
    /// takes the raw format string (a Localization node's output, or another SmartFormat's output) and its
    /// <see cref="VariablesIn"/> input takes <b>many</b> External Variable node outputs, whose values are fed into
    /// SmartFormat as named placeholders (e.g. <c>{Health}</c>). Its <see cref="OutPoint"/> emits the formatted text,
    /// wired wherever a text/Title is expected — the Entry node's Title input, or another SmartFormat's text input.
    /// </summary>
    public class StorySmartFormatNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>The raw SmartFormat template input — accepts a Localization (or SmartFormat) text output.</summary>
        public StoryConnectionPoint LocalizationIn { get; set; } = new() { Name = "Text" };

        /// <summary>The variables input — accepts many External Variable node outputs (one placeholder each).</summary>
        public StoryConnectionPoint VariablesIn { get; set; } = new() { Name = "Variables" };

        /// <summary>The single output port carrying the formatted text (connects to a Title/text input).</summary>
        public StoryConnectionPoint OutPoint { get; set; } = new() { Name = "Text" };

        /// <summary>Letter-case transform applied to the formatted result before the prefix/suffix are wrapped around it.</summary>
        public StoryTextCasing Casing { get; set; } = StoryTextCasing.None;

        /// <summary>Literal text prepended to the (case-transformed) formatted result. Empty adds nothing.</summary>
        public string Prefix { get; set; } = "";

        /// <summary>Literal text appended after the (case-transformed) formatted result. Empty adds nothing.</summary>
        public string Suffix { get; set; } = "";
    }

    /// <summary>A letter-case transform a <see cref="StorySmartFormatNode"/> applies to its formatted output.</summary>
    public enum StoryTextCasing
    {
        None,
        Upper,
        Lower
    }
}
