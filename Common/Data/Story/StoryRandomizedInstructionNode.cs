using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A node that lives only inside a logic node's inner content graph — a <b>medium-switching SmartFormat</b> that
    /// expresses a random choice. It picks one outcome from an authored <b>range</b> and renders it differently per
    /// medium: in the interactive <b>App</b> its <see cref="OutText"/> shows the single drawn value; in the printed
    /// <b>Gamebook</b> it prints the whole D12 roll-to-outcome table (a player physically rolls). The range can vary
    /// by the value wired into <see cref="BranchIn"/> (a <c>CVariable</c>), so different branches draw from different
    /// pools.
    /// <para>
    /// The wired instruction text (App via <see cref="AppTextIn"/>, Gamebook via <see cref="GamebookTextIn"/>) may use
    /// two SmartFormat tokens: <c>{<see cref="ResultToken"/>}</c> — the App picked value, or the Gamebook band table —
    /// and <c>{RandomInstruction}</c> — empty in the App, the localized "Roll a D12" phrase in the Gamebook. The
    /// App-picked value is also exposed on <see cref="OutVariable"/> (a <c>Variable</c> port, App-only) under the same
    /// <see cref="ResultToken"/> name for downstream SmartFormat / Exit inputs.
    /// </para>
    /// </summary>
    public class StoryRandomizedInstructionNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>The App instruction format string — accepts a Localization / SmartFormat text output. Rendered when the target is the App.</summary>
        public StoryConnectionPoint AppTextIn { get; set; } = new() { Name = "App Instruction" };

        /// <summary>The Gamebook instruction format string — accepts a Localization / SmartFormat text output. Rendered when the target is the Gamebook.</summary>
        public StoryConnectionPoint GamebookTextIn { get; set; } = new() { Name = "Gamebook Instruction" };

        /// <summary>
        /// Optional <c>Text</c> input wrapping <b>each</b> Gamebook outcome value before it goes into the D12 band table.
        /// (The App draws a single value, so it needs no per-result format.) The wired text may use
        /// <c>{<see cref="ResultToken"/>}</c> as the placeholder for the raw value — e.g. <c>&lt;var={RandomResult}&gt;</c>
        /// turns each outcome into a variable pill. Wire a <b>Constant String</b> node so the placeholder survives to be
        /// substituted here. When unwired the raw value is used verbatim.
        /// </summary>
        public StoryConnectionPoint GamebookResultFormat { get; set; } = new() { Name = "Gamebook Result Format" };

        /// <summary>
        /// Optional <c>CVariable</c> input selecting which range to draw from. When wired, the author defines one
        /// <see cref="StoryRandomRange"/> per possible branch value (instead of the single <see cref="DefaultRange"/>);
        /// the concrete value is resolved per render / Gamebook section.
        /// </summary>
        public StoryConnectionPoint BranchIn { get; set; } = new() { Name = "Branch" };

        /// <summary>The <c>Text</c> output carrying the medium-correct rendered instruction — connects to a FlowText / Exit text input.</summary>
        public StoryConnectionPoint OutText { get; set; } = new() { Name = "Text" };

        /// <summary>The <c>Variable</c> output (App only) carrying the drawn value under the <see cref="ResultToken"/> token — connects to a SmartFormat / Exit variables input.</summary>
        public StoryConnectionPoint OutVariable { get; set; } = new() { Name = "Result" };

        /// <summary>The SmartFormat token name the random result fills (e.g. <c>RandomResult</c> → <c>{RandomResult}</c>).</summary>
        public string ResultToken { get; set; } = "RandomResult";

        /// <summary>Whether the App keeps the first draw across undo/redo (<see cref="RandomMode.Saved"/>) or re-draws each time (<see cref="RandomMode.Pure"/>). Ignored by the Gamebook (the player physically rolls).</summary>
        public RandomMode RandomMode { get; set; } = RandomMode.Saved;

        /// <summary>How a range whose size does not divide 12 evenly splits the D12 into bands (ignored when the size divides 12).</summary>
        public RandomRemainderMode RemainderMode { get; set; } = RandomRemainderMode.Reroll;

        /// <summary>The range drawn from when <see cref="BranchIn"/> is unwired.</summary>
        public List<string> DefaultRange { get; } = new();

        /// <summary>One range per possible <see cref="BranchIn"/> value, used when the branch is wired.</summary>
        public List<StoryRandomRange> BranchRanges { get; } = new();

        /// <summary>The value shown in the App preview (the App draws the live value at runtime).</summary>
        public string PreviewValue { get; set; } = string.Empty;
    }

    /// <summary>A random range keyed to one possible value of a <see cref="StoryRandomizedInstructionNode.BranchIn"/> source.</summary>
    public class StoryRandomRange
    {
        /// <summary>The branch value this range applies to (matched against the resolved branch value).</summary>
        public string BranchValue { get; set; } = string.Empty;

        /// <summary>The outcomes drawn from for this branch value.</summary>
        public List<string> Values { get; } = new();
    }

    /// <summary>
    /// How a <see cref="StoryRandomizedInstructionNode"/> range whose element count does not divide 12 evenly maps the
    /// twelve D12 faces onto its outcomes. Irrelevant when the count divides 12 (1, 2, 3, 4, 6, 12) — those always
    /// produce equal bands.
    /// </summary>
    public enum RandomRemainderMode
    {
        /// <summary>Equal <c>floor(12 / N)</c> bands; the top out-of-range faces are rerolled (matches the D6 4/5 reroll convention).</summary>
        Reroll,

        /// <summary>Uneven bands covering all twelve faces — the remainder is spread across the first bands (each one face wider).</summary>
        Fill
    }
}
