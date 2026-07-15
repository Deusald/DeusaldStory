using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>A variable a Single-path logic node declares to hand to the next node (see <see cref="StoryLogicNode.DeclaredVariables"/>).</summary>
    public class StoryDeclaredVariable
    {
        public Guid   Id   { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;

        /// <summary>The values this variable can take. Their cartesian product (across all declared variables) dictates how
        /// many Gamebook sections the consuming node expands into; each choice picks one value per variable.</summary>
        public List<string> PossibleValues { get; set; } = new();
    }

    /// <summary>One declared variable's value for a specific <see cref="StoryChoice"/> (Single-path mode).</summary>
    public class StoryChoiceVarValue
    {
        /// <summary>The <see cref="StoryDeclaredVariable.Id"/> this value is for.</summary>
        public Guid   DeclaredVarId { get; set; }
        public string Value         { get; set; } = string.Empty;
    }

    /// <summary>
    /// One continuation a logic node offers — the merged replacement for the old per-exit points and the Choice node.
    /// A logic node owns a list of these on its single Exit node. Each choice has an author <see cref="Name"/>, a
    /// <see cref="TextIn"/> port on the Exit node carrying the player-facing text (empty → "Click here to continue…" /
    /// "To continue go to section …"), and an optional App auto-resolution <see cref="Condition"/> (the locked
    /// <see cref="IsElse"/> fallback carries none). In <see cref="StoryLogicExitMode.ManyPaths"/> the choice has its own
    /// outer <see cref="OuterFlowOut"/> Flow port (wired to the next node); in <see cref="StoryLogicExitMode.SinglePath"/>
    /// all choices share the node's one VFlow output and this choice instead pins <see cref="VariableValues"/> for the
    /// node's declared variables. Ids are stable across edits so wires and Gamebook section tokens survive.
    /// </summary>
    public class StoryChoice
    {
        public Guid   Id   { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;

        /// <summary>The player-facing text input on the Exit node for this choice — accepts a Localization/SmartFormat output.</summary>
        public StoryConnectionPoint TextIn { get; set; } = new() { Name = "Text" };

        /// <summary>The locked fallback choice taken when no earlier choice's condition matched (App auto-resolution).</summary>
        public bool IsElse { get; set; }

        /// <summary>App auto-resolution condition (null for the Else choice, or when the node isn't auto-resolving).</summary>
        public StoryConditionExpr? Condition { get; set; }

        /// <summary>ManyPaths mode — this choice's own outer Flow output on the logic card, wired to the next node.</summary>
        public StoryConnectionPoint OuterFlowOut { get; set; } = new() { Name = "Flow" };

        /// <summary>SinglePath mode — the value this choice pins for each of the node's declared variables.</summary>
        public List<StoryChoiceVarValue> VariableValues { get; set; } = new();
    }
}
