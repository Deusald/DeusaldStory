using System;
using System.Collections.Generic;
using DeusaldStoryCommon;

namespace DeusaldStoryCommon
{
    /// <summary>One boundary port of a blueprint definition — the single source of truth an instance's ports mirror.</summary>
    public readonly struct BlueprintBoundaryPort
    {
        public BlueprintBoundaryPort(Guid definitionPointId, string name, bool isEntry, PortType type)
        {
            DefinitionPointId = definitionPointId;
            Name              = name;
            IsEntry           = isEntry;
            Type              = type;
        }

        /// <summary>The connection-point id inside the definition this boundary port corresponds to.</summary>
        public Guid     DefinitionPointId { get; }
        public string   Name              { get; }
        public bool     IsEntry           { get; }
        public PortType Type              { get; }
    }

    /// <summary>
    /// Enumerates the boundary ports of a blueprint definition (entry side and exit side), so make-into-blueprint,
    /// instance creation, projection, the expander and reconciliation all agree on what ports an instance exposes.
    /// For <see cref="StoryBlueprintKind.Function"/> this returns the typed <b>signature</b> ports only — the LFlow
    /// spine (FlowIn/FlowOut) is handled separately by projection and the expander.
    /// </summary>
    public static class StoryBlueprintBoundary
    {
        public static List<BlueprintBoundaryPort> Enumerate(StoryProject project, StoryBlueprint blueprint)
        {
            List<BlueprintBoundaryPort> ports = new();
            if (project == null || blueprint == null) return ports;

            switch (blueprint.Kind)
            {
                case StoryBlueprintKind.Container:
                    if (project.ContainerNodes.TryGetValue(blueprint.DefinitionNodeId, out StoryContainerNode? container))
                    {
                        foreach (StoryConnectionPoint ep in container.EntryPoints)
                            ports.Add(new BlueprintBoundaryPort(ep.Id, ep.Name, true,
                                ep.FlowKind == StoryPointFlow.VFlow ? PortType.VFlow : PortType.Flow));
                        foreach (StoryConnectionPoint xp in container.ExitPoints)
                            ports.Add(new BlueprintBoundaryPort(xp.Id, xp.Name, false,
                                xp.FlowKind == StoryPointFlow.VFlow ? PortType.VFlow : PortType.Flow));
                    }
                    break;

                case StoryBlueprintKind.Logic:
                    if (project.LogicNodes.TryGetValue(blueprint.DefinitionNodeId, out StoryLogicNode? logic))
                    {
                        ports.Add(new BlueprintBoundaryPort(logic.EntryPoint.Id,
                            UiLang.T(logic.AcceptVariables ? Localization.Editor.Nodes.Ports.variables : Localization.Editor.Nodes.Ports.flow), true,
                            logic.AcceptVariables ? PortType.VFlow : PortType.Flow));

                        if (logic.ExitMode == StoryLogicExitMode.SinglePath)
                            ports.Add(new BlueprintBoundaryPort(logic.VFlowOut.Id, UiLang.T(Localization.Editor.Nodes.Ports.continueLabel), false, PortType.VFlow));
                        else
                            foreach (StoryChoice choice in logic.Choices)
                                ports.Add(new BlueprintBoundaryPort(choice.OuterFlowOut.Id,
                                    string.IsNullOrWhiteSpace(choice.Name) ? UiLang.T(Localization.Editor.Nodes.Ports.choice) : choice.Name, false, PortType.Flow));
                    }
                    break;

                case StoryBlueprintKind.Function:
                    foreach (StorySignaturePort input in blueprint.Inputs)
                        ports.Add(new BlueprintBoundaryPort(input.Id, input.Name, true, input.Type));
                    foreach (StorySignaturePort output in blueprint.Outputs)
                        ports.Add(new BlueprintBoundaryPort(output.Id, output.Name, false, output.Type));
                    break;
            }

            return ports;
        }
    }
}
