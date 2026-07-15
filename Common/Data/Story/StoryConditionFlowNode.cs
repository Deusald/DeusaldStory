using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// A conditional-flow "pair" inside a <b>logic node's inner graph</b>. Like a logic portal, creating one always
    /// spawns <b>two</b> canvas cards from this single object — a <i>Condition</i> card and its paired <i>End condition</i>
    /// card — so each card carries its own position (<see cref="X"/>/<see cref="Y"/> and <see cref="EndX"/>/<see cref="EndY"/>).
    /// It injects an optional block of flow into the spine: the Condition card sits on the LFlow chain
    /// (<see cref="FlowIn"/> → <see cref="ContinueOut"/>) and, when its <see cref="Condition"/> evaluates true, flow first
    /// detours out <see cref="ConditionTrueOut"/>, runs the sub-flow up to the paired <see cref="EndFlowIn"/>, then
    /// returns and continues from <see cref="ContinueOut"/> — an authored <c>if (cond) { … }</c>. Only <b>constant</b>
    /// variables (wired into <see cref="VariablesIn"/>) may feed the condition, so it resolves deterministically at build
    /// time and renders identically in the App and the Gamebook. <see cref="Negate"/> inverts the result.
    /// </summary>
    public class StoryConditionFlowNode
    {
        /// <summary>Identity of this pair and the canvas id of the <b>Condition</b> card.</summary>
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>The canvas id of the paired <b>End condition</b> card (distinct from <see cref="Id"/>).</summary>
        public Guid EndId { get; set; } = Guid.NewGuid();

        public string Name        { get; set; } = string.Empty;
        public string Description  { get; set; } = string.Empty;

        /// <summary>Condition card position.</summary>
        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>End condition card position.</summary>
        public double EndX { get; set; }
        public double EndY { get; set; }

        /// <summary>Condition card LFlow input — wired from the Entry's flow output or a previous flow node.</summary>
        public StoryConnectionPoint FlowIn { get; set; } = new() { Name = "Flow" };

        /// <summary>Condition card Variables input (many-in) — accepts the constant variable outputs the condition tests.</summary>
        public StoryConnectionPoint VariablesIn { get; set; } = new() { Name = "Variables" };

        /// <summary>Condition card LFlow output — the spine always continues here after the (optional) injected block.</summary>
        public StoryConnectionPoint ContinueOut { get; set; } = new() { Name = "Continue" };

        /// <summary>Condition card LFlow output — entered only when the condition is true; runs up to <see cref="EndFlowIn"/>.</summary>
        public StoryConnectionPoint ConditionTrueOut { get; set; } = new() { Name = "Condition true" };

        /// <summary>End condition card LFlow input — the sole port on the paired end card; terminates the injected block.</summary>
        public StoryConnectionPoint EndFlowIn { get; set; } = new() { Name = "Flow" };

        /// <summary>The boolean expression tested at render time (operands referenced by the outputs wired into <see cref="VariablesIn"/>).</summary>
        public StoryConditionExpr Condition { get; set; } = new() { Kind = StoryConditionExprKind.Group };

        /// <summary>When set, the evaluated <see cref="Condition"/> result is inverted before deciding whether to enter the block.</summary>
        public bool Negate { get; set; }
    }
}
