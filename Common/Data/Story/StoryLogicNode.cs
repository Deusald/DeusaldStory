using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// The heart of the system — a stop point in the story that generates content and runs calculations on variables.
    /// The story is played by moving from one logic node to the next. A logic node has exactly one entry point and a
    /// single <b>Exit</b> node. Its inner render graph is a linear flow chain from the Entry to the Exit in which every
    /// node has exactly one flow in and one flow out; a Condition node is the only branch (it detours out and rejoins).
    /// </summary>
    public class StoryLogicNode : IFileWithId
    {
        public Guid   Id              { get; set; } = Guid.NewGuid();
        public string Name            { get; set; } = string.Empty;
        public string Description     { get; set; } = string.Empty;
        public Guid   ParentContainer { get; set; }
        public double X               { get; set; }
        public double Y               { get; set; }

        /// <summary>
        /// When set, this node holds only variable calculations / randomizations — no story-facing screen. In the
        /// app it runs automatically and the player never stops here; in the printed Gamebook it becomes a section
        /// whose body is <b>generated instructions</b> telling players to perform those operations by hand.
        /// </summary>
        public bool GamebookInstructions { get; set; }

        /// <summary>
        /// How this node hands its continuations to the next node(s): <see cref="StoryLogicExitMode.ManyPaths"/>
        /// (one outer output per <see cref="Choices"/> entry), <see cref="StoryLogicExitMode.SinglePath"/> (every
        /// continuation shares <see cref="SingleOut"/> and comes from enumerating <see cref="ChoiceDefinitions"/>),
        /// or <see cref="StoryLogicExitMode.HubPaths"/> (like ManyPaths, rendered as sub-cards / gather-hub cards).
        /// </summary>
        public StoryLogicExitMode ExitMode { get; set; }

        /// <summary>
        /// When <see cref="ExitAutoResolve"/> is set, whether the App auto-picks one choice
        /// (<see cref="StoryExitAutoMode.AutomaticChoice"/>) or uses each choice's condition to gate its visibility
        /// (<see cref="StoryExitAutoMode.ChoiceVisibility"/>). Forced to ChoiceVisibility for HubPaths.
        /// </summary>
        public StoryExitAutoMode ExitAutoMode { get; set; }

        /// <summary>
        /// Whether this node's choices resolve automatically from their conditions (see <see cref="ExitAutoMode"/>).
        /// Replaces the old "is something wired into the Exit's Variables port" signal — with variables global there
        /// is nothing to wire, so auto-resolution is an explicit flag.
        /// </summary>
        public bool ExitAutoResolve { get; set; }

        /// <summary>
        /// The continuations offered by this node in ManyPaths / HubPaths. <b>Unused in SinglePath</b> — there the
        /// continuations are the enumeration of <see cref="ChoiceDefinitions"/>.
        /// </summary>
        public List<StoryChoice> Choices { get; } = new();

        /// <summary>
        /// SinglePath only — at most <see cref="StoryChoiceVariables.MAX_DEFINITIONS"/> definitions describing the
        /// choices the player makes here. Position x writes <c>StoryChoiceVariables.All[x]</c> (ChoiceA/B/C), and the
        /// cartesian product of their entries dimensions the downstream node's Gamebook sections.
        /// </summary>
        public List<StoryChoiceDefinition> ChoiceDefinitions { get; } = new();

        /// <summary>SinglePath only — the single outer output every continuation shares. Kept stable across mode toggles.</summary>
        public StoryConnectionPoint SingleOut { get; set; } = new() { Name = "Continue" };

        /// <summary>This node's single outer input; inside the inner graph it is also the Entry node's flow output.</summary>
        public StoryConnectionPoint EntryPoint { get; set; } = new() { Name = "In" };

        // ── Inner content graph ────────────────────────────────────────────────
        // The logic node opens into its own linear flow graph. The single EntryPoint (above) is the Entry node and the
        // single Exit node terminates the chain at ExitFlowIn. Content nodes and their wiring live here too and are
        // serialized as part of this logic node's file.

        /// <summary>The Exit node's flow input — the render chain terminates here.</summary>
        public StoryConnectionPoint ExitFlowIn { get; set; } = new() { Name = "Flow" };

        // ── Entry node content ─────────────────────────────────────────────────

        /// <summary>The screen title shown for this node. Authored in place — key plus formatting.</summary>
        public StoryTextConfig EntryTitle { get; set; } = new();

        /// <summary>The screen subtitle shown for this node; optional.</summary>
        public StoryTextConfig EntrySubtitle { get; set; } = new();

        /// <summary>
        /// The icon shown in the light/paper theme (a <see cref="StoryImage"/> id). When empty the renderer falls back
        /// to <see cref="DarkIcon"/>, so filling only one slot gives both themes the same icon.
        /// </summary>
        public Guid LightIcon { get; set; }

        /// <summary>The icon shown in the dark theme; falls back to <see cref="LightIcon"/> when empty.</summary>
        public Guid DarkIcon { get; set; }

        /// <summary>Overrides the default "Click here to continue…" label in Automatic-Choice mode.</summary>
        public StoryTextConfig AutoText { get; set; } = new();

        // ── Inner node collections — every node has exactly one flow in and one flow out ──

        /// <summary>Text nodes on the flow chain — the ordered sequence of rendered text blocks.</summary>
        public List<StoryTextNode> TextNodes { get; } = new();

        /// <summary>Split-for-App nodes — each breaks the App render into a new "continue" page (ignored by the Gamebook).</summary>
        public List<StorySplitForAppNode> SplitForAppNodes { get; } = new();

        /// <summary>Set-variable nodes — each assigns a value to a global variable (External or Internal).</summary>
        public List<StorySetVariableNode> SetVariableNodes { get; } = new();

        /// <summary>Constant Variable nodes — each publishes a named constant into this node's variable dictionary.</summary>
        public List<StoryConstantVariableNode> ConstantVariableNodes { get; } = new();

        /// <summary>Randomized Instruction nodes — each renders a random choice (App drawn value / Gamebook D12 band table).</summary>
        public List<StoryRandomizedInstructionNode> RandomizedInstructionNodes { get; } = new();

        /// <summary>Condition-flow pairs — each injects an optional block of flow gated by a condition.</summary>
        public List<StoryConditionFlowNode> ConditionFlowNodes { get; } = new();

        /// <summary>Free-text comment notes placed in this logic node's inner graph (no ports; documentation only).</summary>
        public List<StoryCommentNode> CommentNodes { get; } = new();

        /// <summary>Wires between the inner graph's connection points (Entry/Exit ports and content-node ports).</summary>
        public List<StoryConnection> ContentConnections { get; } = new();

        /// <summary>
        /// Every authored text on this node, in no particular order — the one place the validator and any "find
        /// usages" pass need to look to see which localization keys this node references.
        /// </summary>
        public IEnumerable<StoryTextConfig> AllTextConfigs()
        {
            yield return EntryTitle;
            yield return EntrySubtitle;
            yield return AutoText;

            foreach (StoryChoice choice in Choices)
                yield return choice.Label;

            foreach (StoryTextNode text in TextNodes)
                yield return text.Text;

            foreach (StorySetVariableNode set in SetVariableNodes)
            {
                yield return set.Instruction;
                yield return set.Placeholder;
                yield return set.GamebookValueText;
            }

            foreach (StoryRandomizedInstructionNode random in RandomizedInstructionNodes)
            {
                yield return random.AppText;
                yield return random.GamebookText;
                yield return random.GamebookResultFormat;
            }
        }
    }
}
