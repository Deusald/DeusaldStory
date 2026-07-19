using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// An inner content node on a logic node's flow spine expressing a <b>random choice</b>. It picks one outcome from
    /// an authored <b>range</b> and renders it differently per medium: in the interactive <b>App</b> it shows the single
    /// drawn value; in the printed <b>Gamebook</b> it prints the whole D12 roll-to-outcome table (a player physically
    /// rolls). The range can vary by <see cref="BranchVariableId"/>, so different branches draw from different pools.
    /// <para>
    /// The instruction texts (<see cref="AppText"/> / <see cref="GamebookText"/>) may use two SmartFormat tokens:
    /// <c>{<see cref="ResultToken"/>}</c> — the App picked value, or the Gamebook band table — and
    /// <c>{RandomInstruction}</c> — empty in the App, the localized "Roll a D12" phrase in the Gamebook. The drawn value
    /// is published into the node's variable dictionary under <see cref="ResultToken"/>, and — when
    /// <see cref="SaveToVariable"/> is set — also written to <see cref="TargetVariableId"/>.
    /// </para>
    /// </summary>
    public class StoryRandomizedInstructionNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>Flow input — wired from a previous spine node's flow output.</summary>
        public StoryConnectionPoint FlowIn { get; set; } = new() { Name = "Flow" };

        /// <summary>Flow output — wired to the next spine node's flow input or the Exit.</summary>
        public StoryConnectionPoint FlowOut { get; set; } = new() { Name = "Flow" };

        /// <summary>The App instruction, rendered when the target is the App.</summary>
        public StoryTextConfig AppText { get; set; } = new();

        /// <summary>The Gamebook instruction, rendered when the target is the Gamebook.</summary>
        public StoryTextConfig GamebookText { get; set; } = new();

        /// <summary>
        /// Optional format wrapping <b>each</b> Gamebook outcome value before it goes into the D12 band table. (The App
        /// draws a single value, so it needs no per-result format.) It may use <c>{<see cref="ResultToken"/>}</c> as the
        /// placeholder for the raw value — e.g. <c>&lt;var={RandomResult}&gt;</c> turns each outcome into a variable
        /// pill. When empty the raw value is used verbatim.
        /// </summary>
        public StoryTextConfig GamebookResultFormat { get; set; } = new();

        /// <summary>
        /// Optional variable selecting which range to draw from. When set, the author defines one
        /// <see cref="StoryRandomRange"/> per possible branch value (instead of the single <see cref="DefaultRange"/>);
        /// the concrete value is resolved per render / Gamebook section.
        /// </summary>
        public Guid BranchVariableId { get; set; }

        /// <summary>When set, the drawn value is also written to <see cref="TargetVariableId"/>; otherwise it is only displayed.</summary>
        public bool SaveToVariable { get; set; }

        /// <summary>The variable the drawn value is written to when <see cref="SaveToVariable"/> is set.</summary>
        public Guid TargetVariableId { get; set; }

        /// <summary>The SmartFormat token name the random result fills (e.g. <c>RandomResult</c> → <c>{RandomResult}</c>).</summary>
        public string ResultToken { get; set; } = "RandomResult";

        /// <summary>Whether the App keeps the first draw across undo/redo (<see cref="RandomMode.Saved"/>) or re-draws each time (<see cref="RandomMode.Pure"/>). Ignored by the Gamebook (the player physically rolls).</summary>
        public RandomMode RandomMode { get; set; } = RandomMode.Saved;

        /// <summary>How a range whose size does not divide 12 evenly splits the D12 into bands (ignored when the size divides 12).</summary>
        public RandomRemainderMode RemainderMode { get; set; } = RandomRemainderMode.Reroll;

        /// <summary>The range drawn from when <see cref="BranchVariableId"/> is unset.</summary>
        public List<string> DefaultRange { get; } = new();

        /// <summary>One range per possible <see cref="BranchVariableId"/> value, used when a branch variable is set.</summary>
        public List<StoryRandomRange> BranchRanges { get; } = new();

        /// <summary>The value shown in the App preview (the App draws the live value at runtime).</summary>
        public string PreviewValue { get; set; } = string.Empty;
    }

    /// <summary>A random range keyed to one possible value of a <see cref="StoryRandomizedInstructionNode.BranchVariableId"/> variable.</summary>
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
