using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Small helpers over a logic node's inner graph. Variable <i>values</i> now live in the render's
    /// <see cref="StoryVariableDictionary.Context"/> rather than being chased down wires, so what is left here is
    /// variable lookup, condition evaluation, and the flow-ordered list of Set nodes.
    /// </summary>
    [PublicAPI]
    public static class StoryLogicFlow
    {
        /// <summary>The global variable with <paramref name="id"/> — a built-in, a Choice variable, a catalogued one, or a derived text map.</summary>
        public static StoryVariable? Variable(StoryProject project, Guid id) => StoryVariableCatalog.Resolve(project, id);

        /// <summary>The variable a Set node writes (null when nothing picked or it no longer exists).</summary>
        public static StoryVariable? SetTarget(StoryProject project, StorySetVariableNode set) => Variable(project, set.SelectedVariableId);

        /// <summary>
        /// Evaluates a condition-flow node's <see cref="StoryConditionFlowNode.Condition"/> against the render's
        /// variable dictionary, applying its <see cref="StoryConditionFlowNode.Negate"/> flag. The spine walk calls
        /// this to decide whether to enter the injected "condition true" block.
        /// </summary>
        public static bool EvaluateConditionFlow(StoryConditionFlowNode cf, StoryVariableDictionary.Context ctx)
        {
            bool result = cf.Condition.Evaluate(id => ctx.ById.TryGetValue(id, out string? value) ? value : "");
            return cf.Negate ? !result : result;
        }

        /// <summary>The output wired into <paramref name="toPoint"/>, or <see cref="Guid.Empty"/> when the input is unconnected.</summary>
        public static Guid FromInto(StoryLogicNode logic, Guid toPoint) =>
            logic.ContentConnections.Find(c => c.ToPoint == toPoint)?.FromPoint ?? Guid.Empty;

        /// <summary>
        /// The Set nodes on <paramref name="logic"/>'s flow spine in flow order, then any Set nodes dropped in the
        /// graph but left unwired (they still run when the story visits the node). Both External and Internal sets are
        /// returned; the caller decides which surface a player instruction.
        /// </summary>
        public static List<StorySetVariableNode> SetNodesInOrder(StoryProject project, StoryLogicNode logic, StoryVariableDictionary.Context ctx)
        {
            List<StorySetVariableNode> result  = new();
            HashSet<Guid>              visited = new();

            StoryLogicRenderer.WalkSpine(project, logic, ctx, node =>
            {
                if (node is not StorySetVariableNode set) return;
                result.Add(set);
                visited.Add(set.Id);
            });

            bool Wired(StoryConnectionPoint flowIn, StoryConnectionPoint flowOut) =>
                logic.ContentConnections.Exists(c => c.ToPoint == flowIn.Id || c.FromPoint == flowOut.Id);

            foreach (StorySetVariableNode set in logic.SetVariableNodes)
                if (!visited.Contains(set.Id) && !Wired(set.FlowIn, set.FlowOut))
                    result.Add(set);

            return result;
        }
    }
}
