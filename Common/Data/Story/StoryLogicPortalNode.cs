using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>What kind of signal a logic-node portal carries — the port type its in/out points expose.</summary>
    public enum StoryPortalSignal
    {
        Text,
        Icon,
        Variable
    }

    /// <summary>
    /// A portal "pair" (orange) inside a <b>logic node's inner graph</b>. Unlike a container-level
    /// <see cref="StoryPortalNode"/> (which teleports story flow and is many-in / one-out), a logic portal carries a
    /// <b>value</b> — a <see cref="Signal"/> of Text, Icon or Variable — and is <b>one-in / many-out</b>: a single
    /// <see cref="InPoint"/> receives the value from a producing output, and any number of <see cref="OutPoints"/>
    /// re-emit it to consuming inputs, keeping the inner graph free of long crossing wires. Rendering resolves a
    /// portal-out output back to whatever feeds the portal's in (see <see cref="StoryLogicNode.ResolvePortalSource"/>).
    /// Each in/out is drawn as its own canvas node, so every point carries an <c>X</c>/<c>Y</c>.
    /// </summary>
    public class StoryLogicPortalNode
    {
        public Guid   Id          { get; set; } = Guid.NewGuid();
        public string Name        { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>The value type carried by this portal — drives the port type (and colour) of its in/out points.</summary>
        public StoryPortalSignal Signal { get; set; } = StoryPortalSignal.Variable;

        /// <summary>The single "portal in" node — a value arrives here from a producing output.</summary>
        public StoryConnectionPoint InPoint { get; set; } = new() { Name = "In" };

        /// <summary>The "portal out" nodes (at least one) — each re-emits the value fed into <see cref="InPoint"/>.</summary>
        public List<StoryConnectionPoint> OutPoints { get; } = new();
    }
}
