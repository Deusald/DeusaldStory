using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// The non-deletable node drawn inside a logic node's inner graph when the node has
    /// <see cref="StoryLogicNode.AcceptVariables"/> on and its <see cref="StoryLogicNode.VariablesIn"/> is wired to an
    /// upstream Single-path node's VFlow output. It exposes the incoming variables the upstream node declares
    /// (<see cref="StoryLogicNode.DeclaredVariables"/>) as one <c>CVariable</c> output each — the value is fixed by the
    /// upstream choice that led here, so within any generated Gamebook section it is a constant. The output ports are
    /// synthesized in the projection using each upstream <see cref="StoryDeclaredVariable.Id"/> as the port id, so
    /// connections stay stable across edits. There is exactly one per logic node (always present, like the Entry);
    /// it is only drawn when relevant. This node holds only its canvas position.
    /// </summary>
    public class StoryPrevExitVariableNode
    {
        public Guid   Id { get; set; } = Guid.NewGuid();
        public double X  { get; set; }
        public double Y  { get; set; }
    }
}
