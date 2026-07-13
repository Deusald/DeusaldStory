using System;

namespace DeusaldStoryCommon
{
    /// <summary>How a <see cref="StoryConditionNode"/> compares the wired variable's value against its <see cref="StoryConditionNode.CompareValue"/>.</summary>
    public enum StoryConditionOperator
    {
        /// <summary>String match (case-insensitive).</summary>
        Equal,

        /// <summary>String mismatch (case-insensitive).</summary>
        NotEqual,

        /// <summary>Both sides parsed to integers — variable &lt; value.</summary>
        LessThan,

        /// <summary>Both sides parsed to integers — variable &gt; value.</summary>
        GreaterThan,

        /// <summary>Both sides parsed to integers — variable ≤ value.</summary>
        LessOrEqual,

        /// <summary>Both sides parsed to integers — variable ≥ value.</summary>
        GreaterOrEqual
    }

    /// <summary>
    /// An inner content node on a logic node's flow spine that <b>branches the flow</b> by testing a variable. The
    /// value wired into <see cref="VariableIn"/> (an External Variable node's output, or the Prev-Exit Selection) is
    /// compared against <see cref="CompareValue"/> using <see cref="Operator"/>; flow arriving at <see cref="FlowIn"/>
    /// then continues out of <see cref="FlowTrue"/> when the test passes and <see cref="FlowFalse"/> when it fails.
    /// The four ordering operators parse both sides to integers (and fail the test when either isn't a number);
    /// Equal / NotEqual compare as case-insensitive strings. Evaluated the same way in both the App and the printed
    /// Gamebook (each Gamebook section fixes the variable values), so no printed instruction is produced.
    /// </summary>
    public class StoryConditionNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }

        /// <summary>Which comparison decides the branch.</summary>
        public StoryConditionOperator Operator { get; set; }

        /// <summary>The literal the wired variable's value is compared against.</summary>
        public string CompareValue { get; set; } = string.Empty;

        /// <summary>Flow input — wired from a previous flow node's output.</summary>
        public StoryConnectionPoint FlowIn { get; set; } = new() { Name = "Flow" };

        /// <summary>Variable input — the value tested (an External Variable output or the Prev-Exit Selection).</summary>
        public StoryConnectionPoint VariableIn { get; set; } = new() { Name = "Variable" };

        /// <summary>Flow output taken when the test passes.</summary>
        public StoryConnectionPoint FlowTrue { get; set; } = new() { Name = "True" };

        /// <summary>Flow output taken when the test fails.</summary>
        public StoryConnectionPoint FlowFalse { get; set; } = new() { Name = "False" };
    }
}
