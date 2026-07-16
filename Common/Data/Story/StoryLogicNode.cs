using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// The heart of the system — a stop point in the story that generates content and runs calculations on variables.
    /// The story is played by moving from one logic node to the next. A logic node has exactly one entry point and a
    /// single <b>Exit</b> node carrying its list of <see cref="Choices"/> (the continuations). Its inner render graph
    /// is a linear <c>LFlow</c> chain from the Entry to the Exit node.
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
        /// (one outer Flow output per choice) or <see cref="StoryLogicExitMode.SinglePath"/> (one outer VFlow output
        /// carrying the <see cref="DeclaredVariables"/>, each choice pinning their values).
        /// </summary>
        public StoryLogicExitMode ExitMode { get; set; }

        /// <summary>The continuations offered by this node's single Exit node (replaces the old per-exit points + Choice node).</summary>
        public List<StoryChoice> Choices { get; } = new();

        /// <summary>SinglePath only — the variables this node declares and hands to the next node over its VFlow output.</summary>
        public List<StoryDeclaredVariable> DeclaredVariables { get; } = new();

        /// <summary>
        /// AcceptVariables contract — when non-empty, the exact set of incoming variables this node <b>expects</b> to
        /// receive (name + possible values), owned by this node so its Prev Exit Variable ports and inner wiring stay
        /// stable regardless of which upstream is wired in. Essential for reusable Logic blueprints: an instance's
        /// upstream must provide exactly this set (validated by name + possible values). Empty ⇒ adapt live to whatever
        /// upstream is wired (the legacy behaviour), unsafe for blueprints.
        /// </summary>
        public List<StoryDeclaredVariable> ExpectedVariables { get; } = new();

        /// <summary>SinglePath only — the single outer VFlow output all choices share. Kept stable across mode toggles.</summary>
        public StoryConnectionPoint VFlowOut { get; set; } = new() { Name = "Continue" };

        /// <summary>
        /// When set, this node gains a second (VFlow) input port, <see cref="VariablesIn"/>, that accepts an upstream
        /// SinglePath node's declared variables. Wiring it in surfaces the <see cref="PrevExitVariable"/> node inside
        /// this node's inner graph, exposing those variables as constants.
        /// </summary>
        public bool AcceptVariables { get; set; }

        /// <summary>The second (VFlow) input port — accepts an upstream node's declared variables. Shown when <see cref="AcceptVariables"/>.</summary>
        public StoryConnectionPoint VariablesIn { get; set; } = new() { Name = "Variables" };

        /// <summary>The always-present Prev Exit Variable node, drawn inside the inner graph when <see cref="AcceptVariables"/> is set and <see cref="VariablesIn"/> is wired.</summary>
        public StoryPrevExitVariableNode PrevExitVariable { get; set; } = new();

        public StoryConnectionPoint EntryPoint { get; set; } = new() { Name = "In" };

        // ── Inner content graph ────────────────────────────────────────────────
        // The logic node opens into its own linear LFlow graph. The single EntryPoint (above) is the Entry node — with
        // three extra config inputs, Title, Subtitle and Icon — and the single Exit node terminates the chain, carrying
        // one Text input per choice plus the auto-resolution inputs. Content nodes (Localization/Icon/SmartFormat/…)
        // and their wiring live here too and are serialized as part of this logic node's file.

        /// <summary>The Entry node's Title input port — accepts a Localization node's output.</summary>
        public StoryConnectionPoint TitleIn { get; set; } = new() { Name = "Title" };

        /// <summary>The Entry node's Subtitle input port — accepts a Localization node's output; optional.</summary>
        public StoryConnectionPoint SubtitleIn { get; set; } = new() { Name = "Subtitle" };

        /// <summary>The Entry node's Icon input port — accepts an Icon node's output.</summary>
        public StoryConnectionPoint IconIn { get; set; } = new() { Name = "Icon" };

        /// <summary>The Exit node's LFlow input port — the render chain terminates here.</summary>
        public StoryConnectionPoint ExitLFlowIn { get; set; } = new() { Name = "Flow" };

        /// <summary>The Exit node's Variables input (many-in) — wire variables here to enable App auto-resolution of the choice.</summary>
        public StoryConnectionPoint ExitVariablesIn { get; set; } = new() { Name = "Variables" };

        /// <summary>The Exit node's single Auto-text input — overrides the default "Click here to continue…" button label in auto mode.</summary>
        public StoryConnectionPoint ExitAutoTextIn { get; set; } = new() { Name = "Text" };

        /// <summary>Localization nodes placed in the inner graph (each feeds a Title/Text input).</summary>
        public List<StoryLocalizationNode> LocalizationNodes { get; } = new();

        /// <summary>Icon nodes placed in the inner graph (each feeds an Icon input).</summary>
        public List<StoryIconNode> IconNodes { get; } = new();

        /// <summary>Light/Dark switch nodes placed in the inner graph (each picks between two icons by theme).</summary>
        public List<StoryLightDarkSwitchNode> LightDarkSwitchNodes { get; } = new();

        /// <summary>SmartFormat nodes placed in the inner graph (each formats a text with connected variable values).</summary>
        public List<StorySmartFormatNode> SmartFormatNodes { get; } = new();

        /// <summary>External Variable nodes placed in the inner graph (each feeds a variable value into a SmartFormat/Exit input).</summary>
        public List<StoryExternalVariableNode> ExternalVariableNodes { get; } = new();

        /// <summary>Get Variable nodes placed in the inner graph (each reads a registered storage variable — App value / Gamebook slot tag).</summary>
        public List<StoryGetVariableNode> GetVariableNodes { get; } = new();

        /// <summary>Constant Variable nodes placed in the inner graph (each supplies a named constant value into a SmartFormat/Exit input).</summary>
        public List<StoryConstantVariableNode> ConstantVariableNodes { get; } = new();

        /// <summary>FlowText nodes placed in the inner graph — the LFlow chain that renders text blocks in order.</summary>
        public List<StoryFlowTextNode> FlowTextNodes { get; } = new();

        /// <summary>Split-for-App nodes on the LFlow chain — each breaks the App render into a new "continue" page (ignored by the Gamebook).</summary>
        public List<StorySplitForAppNode> SplitForAppNodes { get; } = new();

        /// <summary>Register-variable nodes on the LFlow chain — each claims a physical storage slot for a new variable.</summary>
        public List<StoryRegisterVariableNode> RegisterVariableNodes { get; } = new();

        /// <summary>Set-variable nodes on the LFlow chain — each assigns a value to an already-registered variable.</summary>
        public List<StorySetVariableNode> SetVariableNodes { get; } = new();

        /// <summary>Unregister-variable nodes on the LFlow chain — each releases a registered variable and frees its slot.</summary>
        public List<StoryUnregisterVariableNode> UnregisterVariableNodes { get; } = new();

        /// <summary>Set-external-variable nodes on the LFlow chain — each assigns a value to a story-wide external variable.</summary>
        public List<StorySetExternalVariableNode> SetExternalVariableNodes { get; } = new();

        /// <summary>Portal pairs on the inner graph — one-in / many-out relays that carry a Text/Icon/Variable value across the graph.</summary>
        public List<StoryLogicPortalNode> LogicPortalNodes { get; } = new();

        /// <summary>Condition-flow pairs on the LFlow chain — each injects an optional block of flow gated by a constant-variable condition.</summary>
        public List<StoryConditionFlowNode> ConditionFlowNodes { get; } = new();

        /// <summary>Free-text comment notes placed in this logic node's inner graph (no ports; documentation only).</summary>
        public List<StoryCommentNode> CommentNodes { get; } = new();

        /// <summary>Function-blueprint instances on the LFlow chain — each inlines a reusable pure-computation subgraph.</summary>
        public List<StoryFunctionInstanceNode> FunctionInstanceNodes { get; } = new();

        /// <summary>Wires between the inner graph's connection points (Entry/Exit ports and content-node ports).</summary>
        public List<StoryConnection> ContentConnections { get; } = new();

        /// <summary>
        /// Resolves <paramref name="fromPoint"/> through any logic portal it belongs to: when it is a portal-out output,
        /// follows the portal back to whatever output feeds the portal's in (transitively). Any non-portal point — and a
        /// portal whose in is unwired — is returned unchanged, so callers can treat every output source uniformly.
        /// </summary>
        public Guid ResolvePortalSource(Guid fromPoint, int depth = 0)
        {
            if (fromPoint == Guid.Empty || depth > 32) return fromPoint;
            foreach (StoryLogicPortalNode portal in LogicPortalNodes)
                if (portal.OutPoints.Exists(o => o.Id == fromPoint))
                {
                    Guid feed = ContentConnections.Find(c => c.ToPoint == portal.InPoint.Id)?.FromPoint ?? Guid.Empty;
                    return ResolvePortalSource(feed, depth + 1);
                }
            return fromPoint;
        }
    }
}
