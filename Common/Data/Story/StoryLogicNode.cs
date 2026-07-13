using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// The heart of the system — a stop point in the story that generates content and runs calculations on
    /// variables. The story is played by moving from one logic node to the next. A logic node has exactly one
    /// entry point and at least one exit point (a branch the story can take from here).
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
        /// whose body is <b>generated instructions</b> telling players to perform those operations by hand
        /// (roll a die, note a value, jump to a section). Today the only operation source is the node's External
        /// Variables and their Gamebook condition keys; future interaction blocks contribute their own baked keys.
        /// </summary>
        public bool GamebookInstructions { get; set; }

        public StoryConnectionPoint       EntryPoint { get; set; } = new() { Name = "In" };
        public List<StoryConnectionPoint> ExitPoints { get; }      = new();

        // ── Inner content graph ────────────────────────────────────────────────
        // The logic node opens into its own graph. The single EntryPoint (above) is drawn there as the Entry
        // node — with three extra config inputs, Title, Subtitle and Icon — and each ExitPoint is drawn as its
        // own Exit node, reusing their X/Y as inner canvas positions and their ids as the inner flow-port ids
        // (exactly how a container reuses its boundary points inside itself). Localization/Icon nodes and their
        // wiring live here too and are serialized as part of this logic node's file.

        /// <summary>The Entry node's Title input port — accepts a Localization node's output.</summary>
        public StoryConnectionPoint TitleIn { get; set; } = new() { Name = "Title" };

        /// <summary>The Entry node's Subtitle input port — accepts a Localization node's output; optional.</summary>
        public StoryConnectionPoint SubtitleIn { get; set; } = new() { Name = "Subtitle" };

        /// <summary>The Entry node's Icon input port — accepts an Icon node's output.</summary>
        public StoryConnectionPoint IconIn { get; set; } = new() { Name = "Icon" };

        /// <summary>Localization nodes placed in the inner graph (each feeds a Title input).</summary>
        public List<StoryLocalizationNode> LocalizationNodes { get; } = new();

        /// <summary>Icon nodes placed in the inner graph (each feeds an Icon input).</summary>
        public List<StoryIconNode> IconNodes { get; } = new();

        /// <summary>Light/Dark switch nodes placed in the inner graph (each picks between two icons by theme).</summary>
        public List<StoryLightDarkSwitchNode> LightDarkSwitchNodes { get; } = new();

        /// <summary>SmartFormat nodes placed in the inner graph (each formats a text with connected variable values).</summary>
        public List<StorySmartFormatNode> SmartFormatNodes { get; } = new();

        /// <summary>External Variable nodes placed in the inner graph (each feeds a variable value into a SmartFormat node).</summary>
        public List<StoryExternalVariableNode> ExternalVariableNodes { get; } = new();

        /// <summary>FlowText nodes placed in the inner graph — the flow spine that renders text blocks in order.</summary>
        public List<StoryFlowTextNode> FlowTextNodes { get; } = new();

        /// <summary>Register-variable nodes on the flow spine — each claims a physical storage slot for a new variable.</summary>
        public List<StoryRegisterVariableNode> RegisterVariableNodes { get; } = new();

        /// <summary>Set-variable nodes on the flow spine — each assigns a value to an already-registered variable.</summary>
        public List<StorySetVariableNode> SetVariableNodes { get; } = new();

        /// <summary>Unregister-variable nodes on the flow spine — each releases a registered variable and frees its slot.</summary>
        public List<StoryUnregisterVariableNode> UnregisterVariableNodes { get; } = new();

        /// <summary>Wires between the inner graph's connection points (Entry/Exit ports and content-node ports).</summary>
        public List<StoryConnection> ContentConnections { get; } = new();
    }
}
