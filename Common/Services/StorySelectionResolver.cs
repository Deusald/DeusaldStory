using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Resolves the variables a <see cref="StoryLogicNode.AcceptVariables">receiving</see> logic node reads from an
    /// upstream <see cref="StoryLogicExitMode.SinglePath"/> node over its VFlow output. It follows the container wire
    /// arriving at the receiver's <see cref="StoryLogicNode.VariablesIn"/> back to the upstream node's
    /// <see cref="StoryLogicNode.VFlowOut"/> and exposes that node's declared variables (surfaced by the receiver's
    /// Prev Exit Variable node as constants) and the value each upstream choice pins — the Gamebook expands the
    /// receiver into one section per upstream choice.
    /// </summary>
    [PublicAPI]
    public static class StorySelectionResolver
    {
        /// <summary>The upstream SinglePath node feeding <paramref name="logic"/>'s Variables input, or null when none is wired.</summary>
        public static StoryLogicNode? SourceNode(StoryProject project, StoryLogicNode logic)
        {
            if (!logic.AcceptVariables) return null;
            if (!project.ContainerNodes.TryGetValue(logic.ParentContainer, out StoryContainerNode? parent)) return null;

            // The VFlow (variables) arrives at the node's single entry when it accepts variables.
            StoryConnection? wire = parent.Connections.Find(c => c.ToPoint == logic.EntryPoint.Id);
            if (wire is null) return null;

            return project.LogicNodes.Values.FirstOrDefault(l =>
                l.ExitMode == StoryLogicExitMode.SinglePath && l.VFlowOut.Id == wire.FromPoint);
        }

        /// <summary>The declared variables the upstream node hands <paramref name="logic"/> (empty when nothing is wired in).</summary>
        public static List<StoryDeclaredVariable> IncomingVariables(StoryProject project, StoryLogicNode logic) =>
            SourceNode(project, logic)?.DeclaredVariables ?? new List<StoryDeclaredVariable>();

        /// <summary>The value each of <paramref name="source"/>'s declared variables takes for one of its <paramref name="choice"/>s, keyed by declared-variable id.</summary>
        public static Dictionary<Guid, string> ValuesForChoice(StoryLogicNode source, StoryChoice choice)
        {
            Dictionary<Guid, string> map = new();
            foreach (StoryDeclaredVariable dv in source.DeclaredVariables)
                map[dv.Id] = choice.VariableValues.Find(v => v.DeclaredVarId == dv.Id)?.Value ?? "";
            return map;
        }
    }
}
