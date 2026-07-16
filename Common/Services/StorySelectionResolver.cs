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

        /// <summary>
        /// The variables <paramref name="logic"/> receives, as its Prev Exit Variable exposes them. In <b>contract</b>
        /// mode (<see cref="StoryLogicNode.ExpectedVariables"/> non-empty) these are the node's own declared expectation
        /// — stable ids that survive rewiring and blueprint reuse; otherwise the live upstream's declared variables.
        /// </summary>
        public static List<StoryDeclaredVariable> IncomingVariables(StoryProject project, StoryLogicNode logic)
        {
            if (!logic.AcceptVariables) return new List<StoryDeclaredVariable>();
            if (logic.ExpectedVariables.Count > 0) return logic.ExpectedVariables;
            return SourceNode(project, logic)?.DeclaredVariables ?? new List<StoryDeclaredVariable>();
        }

        /// <summary>
        /// The value bound to a Prev Exit Variable output <paramref name="portId"/> from the section/preview
        /// <paramref name="values"/> (which are keyed by the upstream's declared-variable ids). In contract mode the
        /// port is an <see cref="StoryLogicNode.ExpectedVariables"/> id, so it is mapped to the upstream variable of the
        /// same <b>name</b>; otherwise the port id is the upstream id directly.
        /// </summary>
        public static string IncomingValue(StoryProject project, StoryLogicNode logic, Guid portId, IReadOnlyDictionary<Guid, string> values)
        {
            if (logic.ExpectedVariables.Count > 0)
            {
                StoryDeclaredVariable? expected = logic.ExpectedVariables.Find(d => d.Id == portId);
                if (expected is null) return "";
                StoryDeclaredVariable? upstream = SourceNode(project, logic)?.DeclaredVariables
                    .Find(v => string.Equals(v.Name, expected.Name, StringComparison.Ordinal));
                return upstream is not null && values.TryGetValue(upstream.Id, out string? v) ? v : "";
            }
            return values.TryGetValue(portId, out string? val) ? val : "";
        }

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
