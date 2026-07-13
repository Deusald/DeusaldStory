using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Resolves the <b>Selection</b> exit-variable that a <see cref="StoryLogicNode.AcceptExitVariable">receiving</see>
    /// logic node reads from an upstream <see cref="StoryLogicExitMode.SingleSelection"/> node. It follows the
    /// container wire arriving at the receiver's <see cref="StoryLogicNode.ExitVariableIn"/> back to the upstream
    /// node's <see cref="StoryLogicNode.SelectionVarOut"/> and exposes the upstream exits, the value each exit emits
    /// (honouring the receiver's remap), and a synthetic <see cref="StoryVariable"/> the Gamebook uses to expand the
    /// receiver into one section per exit value. Shared by the renderer/preview (this project) and the editor.
    /// </summary>
    [PublicAPI]
    public static class StorySelectionResolver
    {
        /// <summary>
        /// The upstream node's exit points feeding <paramref name="logic"/>'s exit-variable input — the possible
        /// incoming Selection values — or null when the node doesn't accept a variable or nothing is wired in.
        /// </summary>
        public static List<StoryConnectionPoint>? SourceExits(StoryProject project, StoryLogicNode logic)
        {
            if (!logic.AcceptExitVariable) return null;
            if (!project.ContainerNodes.TryGetValue(logic.ParentContainer, out StoryContainerNode? parent)) return null;

            StoryConnection? wire = parent.Connections.Find(c => c.ToPoint == logic.ExitVariableIn.Id);
            if (wire is null) return null;

            StoryLogicNode? source = project.LogicNodes.Values.FirstOrDefault(l => l.SelectionVarOut.Id == wire.FromPoint);
            return source?.ExitPoints;
        }

        /// <summary>The value the receiver emits for an upstream <paramref name="exit"/> — its remap when enabled and set, else the exit name.</summary>
        public static string ValueFor(StoryPrevExitVariableNode prev, StoryConnectionPoint exit)
        {
            if (prev.RemapEnabled)
            {
                StoryPrevExitRemap? remap = prev.Remaps.Find(r => r.SourceExitId == exit.Id);
                if (remap is not null && !string.IsNullOrWhiteSpace(remap.Value)) return remap.Value;
            }
            return string.IsNullOrWhiteSpace(exit.Name) ? "" : exit.Name;
        }

        /// <summary>
        /// A synthetic story variable representing <paramref name="logic"/>'s Selection dimension: id =
        /// <see cref="StoryPrevExitVariableNode.Id"/>, name = the local variable name, possible values = the (remapped,
        /// de-duplicated) upstream exit values. Null when the node doesn't accept a wired-in Selection.
        /// </summary>
        public static StoryVariable? SelectionVariable(StoryProject project, StoryLogicNode logic)
        {
            List<StoryConnectionPoint>? exits = SourceExits(project, logic);
            if (exits is null) return null;

            StoryPrevExitVariableNode prev = logic.PrevExitVariable;
            List<string> values = exits.Select(e => ValueFor(prev, e)).Distinct().ToList();

            return new StoryVariable
            {
                Id             = prev.Id,
                Name           = string.IsNullOrWhiteSpace(prev.VariableName) ? "Selection" : prev.VariableName,
                PossibleValues = values
            };
        }
    }
}
