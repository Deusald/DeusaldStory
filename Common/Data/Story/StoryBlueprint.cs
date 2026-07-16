using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>Which kind of node a blueprint defines.</summary>
    public enum StoryBlueprintKind
    {
        /// <summary>A reusable container subgraph. Instances live on a container's canvas.</summary>
        Container,

        /// <summary>A reusable logic node (with its inner content graph). Instances live on a container's canvas.</summary>
        Logic,

        /// <summary>A reusable pure-computation subgraph with a typed input/output signature and no screen.
        /// Instances live inside a logic node's inner content graph.</summary>
        Function
    }

    /// <summary>
    /// One typed port of a <see cref="StoryBlueprintKind.Function"/> blueprint's signature. Its <see cref="Id"/>
    /// doubles as the real connection-point id used inside the function definition's inner graph (the point that
    /// instance port maps reference through <see cref="StoryBlueprintPortMap.DefinitionPointId"/>).
    /// </summary>
    public class StorySignaturePort
    {
        public Guid     Id   { get; set; } = Guid.NewGuid();
        public string   Name { get; set; } = string.Empty;
        public PortType Type { get; set; } = PortType.Data;
    }

    /// <summary>
    /// A reusable node template. The blueprint's body is a normal <see cref="StoryContainerNode"/> or
    /// <see cref="StoryLogicNode"/> stored <b>out of tree</b> (its <c>ParentContainer</c> is <see cref="Guid.Empty"/>
    /// so it is never reached by a root-anchored walk); this descriptor supplies its identity for the library and for
    /// the lightweight instance nodes that reference it. Editing the definition changes every instance.
    /// </summary>
    public class StoryBlueprint : IFileWithId
    {
        public Guid               Id             { get; set; } = Guid.NewGuid();
        public string             Name           { get; set; } = string.Empty;
        public string             Description    { get; set; } = string.Empty;
        public StoryBlueprintKind Kind           { get; set; }

        /// <summary>The out-of-tree definition node — a <see cref="StoryContainerNode"/> (Container) or
        /// <see cref="StoryLogicNode"/> (Logic / Function).</summary>
        public Guid DefinitionNodeId { get; set; }

        /// <summary><see cref="StoryBlueprintKind.Function"/> only — the typed input ports of the signature.</summary>
        public List<StorySignaturePort> Inputs { get; } = new();

        /// <summary><see cref="StoryBlueprintKind.Function"/> only — the typed output ports of the signature.</summary>
        public List<StorySignaturePort> Outputs { get; } = new();
    }
}
