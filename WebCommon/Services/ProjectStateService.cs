using System.IO;
using DeusaldLocalizerCommon;
using DeusaldStoryCommon;
using JetBrains.Annotations;

namespace DeusaldStoryWeb;

/// <summary>
/// Holds the currently open project and active user for the lifetime of the app session.
/// Inject as a singleton so all pages share the same state.
/// </summary>
[PublicAPI]
public partial class ProjectStateService(
    RecentProjectsStore recents,
    IProjectStoreFactory storeFactory,
    IProjectLocationService location)
{
    // ── State ────────────────────────────────────────────────────────────────

    /// <summary>The currently loaded project. Null when no project is open.</summary>
    public StoryProject? CurrentProject { get; private set; }

    /// <summary>Path on disk where the current project was loaded from / last saved to.</summary>
    public string? CurrentProjectPath { get; private set; }

    /// <summary>
    /// The localization project the current story is linked to, opened via the shared library. Null when no
    /// project is open or its link could not be resolved (the open flow blocks and re-links before it gets here).
    /// </summary>
    public LocProject? CurrentLocalization { get; private set; }

    public HashSet<Guid> ChangedFileIds { get; } = new();
    
    /// <summary>True when a project is open and ready to use.</summary>
    public bool HasProject => CurrentProject is not null;

    /// <summary>True when there are unsaved changes.</summary>
    public bool IsDirty { get; private set; }

    // ── Events ───────────────────────────────────────────────────────────────

    /// <summary>Fires whenever the open project changes (load, close, new).</summary>
    public event Action? ProjectChanged;

    /// <summary>Fires whenever IsDirty changes.</summary>
    public event Action? DirtyStateChanged;

    /// <summary>
    /// Fires every time the project's data is mutated via MarkDirty(), even if
    /// IsDirty was already true. Use this (instead of DirtyStateChanged) when a
    /// component needs to refresh derived data — like translation progress —
    /// after every edit, not just the first one after a save.
    /// </summary>
    public event Action? ProjectDataChanged;

    // ── Construction ───────────────────────────────────────────────────────────

    /// <summary>The file store for the currently open project's location handle.</summary>
    private IProjectFileStore _CurrentStore => storeFactory.Create(CurrentProjectPath!);

    // ── Actions ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new story linked to <paramref name="localizationReference"/> (the loc project the user just
    /// picked). The reference was validated by the picker, so its loc project is opened and held here.
    /// </summary>
    public async Task CreateNewProjectAsync(string name, string slug, string description, string localizationReference)
    {
        StoryProject newProject = new()
        {
            Metadata = new StoryProjectMetadata
            {
                Name                    = name,
                Slug                    = slug,
                Description             = description,
                LocalizationProjectPath = localizationReference,
                UpdatedAt               = DateTime.UtcNow
            }
        };

        // Every project has a Root container. It holds the story's Start (entry) and End (exit) — both
        // non-deletable — and the story plays from Start until it reaches End.
        StoryContainerNode root = new() { Name = "Root" };
        root.EntryPoints.Add(new StoryConnectionPoint { Name = "Start", X = 40,  Y = 220 });
        root.ExitPoints.Add(new StoryConnectionPoint  { Name = "End",   X = 640, Y = 220 });
        newProject.ContainerNodes.Add(root.Id, root);
        newProject.Metadata.RootStoryContainerNodeId = root.Id;

        CurrentProject      = newProject;
        CurrentProjectPath  = null;
        CurrentLocalization = await ResolveLocalizationAsync(newProject);
        IsDirty             = true;
        ChangedFileIds.Clear();
        ChangedFileIds.Add(root.Id);
        ResetHistory();
        ProjectChanged?.Invoke();
        DirtyStateChanged?.Invoke();
    }

#if DEBUG
    /// <summary>
    /// DEBUG ONLY. Spins up a throwaway in-memory test project (no localization link) with a logic node and a
    /// nested container in the root, pre-wired Start → logic → container → End, so the editor can be exercised
    /// without the localization-picker flow. Shared by every host's Home debug button so there's a single place
    /// the test project is defined. Callers navigate to the editor afterwards.
    /// </summary>
    public async Task CreateDebugProjectAsync()
    {
        await CreateNewProjectAsync("Debug Test", "debug-test", "Throwaway test project", "");

        StoryProject project = CurrentProject!;
        Guid         root    = project.Metadata.RootStoryContainerNodeId;

        StoryLogicNode logic = AddLogicNode(root, "Say hello", "A test logic node",
            new StoryConnectionPoint { Name = "In" }, 340, 120);
        StoryContainerNode container = AddContainerNode(root, "Test Container", "A nested container",
            new[] { new StoryConnectionPoint { Name = "In" } },
            new[] { new StoryConnectionPoint { Name = "Out" } }, 340, 340);

        // Pre-wire Start → logic → container → End so edges are visible immediately.
        StoryContainerNode rootNode = project.ContainerNodes[root];
        Connect(root, rootNode.EntryPoints[0].Id, logic.EntryPoint.Id);
        Connect(root, logic.Choices[0].OuterFlowOut.Id, container.EntryPoints[0].Id);
        Connect(root, container.ExitPoints[0].Id, rootNode.ExitPoints[0].Id);

        // The debug scaffold isn't a user edit — don't make it undoable.
        ResetHistory();
    }
#endif

    /// <summary>
    /// Adds a new logic node to <paramref name="parentContainerId"/>, marks it dirty and notifies listeners.
    /// The node is placed at (<paramref name="x"/>, <paramref name="y"/>) on the container's canvas.
    /// </summary>
    public StoryLogicNode AddLogicNode(
        Guid                              parentContainerId,
        string                            name,
        string                            description,
        StoryConnectionPoint              entryPoint,
        double                            x,
        double                            y,
        bool                              gamebookInstructions = false,
        StoryLogicExitMode                exitMode             = StoryLogicExitMode.ManyPaths,
        bool                              acceptVariables      = false)
    {
        using var _ = Edit(); // node + parent container change together
        StoryLogicNode node = new()
        {
            Name                 = name,
            Description          = description,
            ParentContainer      = parentContainerId,
            EntryPoint           = entryPoint,
            X                    = x,
            Y                    = y,
            GamebookInstructions = gamebookInstructions,
            ExitMode             = exitMode,
            AcceptVariables      = acceptVariables
        };
        // Seed one default choice so the node has a continuation port from the start.
        node.Choices.Add(new StoryChoice { Name = "Continue" });

        // Seed inner-graph positions so the Entry/Exit nodes don't stack at the origin when first opened.
        LayoutLogicInnerPoints(node);

        CurrentProject!.LogicNodes.Add(node.Id, node);

        if (CurrentProject.ContainerNodes.TryGetValue(parentContainerId, out StoryContainerNode? parent))
            parent.Logic.Add(node.Id);

        MarkKeyDirty(node.Id);
        MarkKeyDirty(parentContainerId);
        return node;
    }

    /// <summary>
    /// Adds a new (blue) child container to <paramref name="parentContainerId"/> at
    /// (<paramref name="x"/>, <paramref name="y"/>), marks it dirty and notifies listeners.
    /// </summary>
    public StoryContainerNode AddContainerNode(
        Guid                              parentContainerId,
        string                            name,
        string                            description,
        IEnumerable<StoryConnectionPoint> entryPoints,
        IEnumerable<StoryConnectionPoint> exitPoints,
        double                            x,
        double                            y)
    {
        using var _ = Edit(); // node + parent container change together
        StoryContainerNode node = new()
        {
            Name            = name,
            Description     = description,
            ParentContainer = parentContainerId,
            X               = x,
            Y               = y
        };
        node.EntryPoints.AddRange(entryPoints);
        node.ExitPoints.AddRange(exitPoints);

        CurrentProject!.ContainerNodes.Add(node.Id, node);

        if (CurrentProject.ContainerNodes.TryGetValue(parentContainerId, out StoryContainerNode? parent))
            parent.Containers.Add(node.Id);

        MarkKeyDirty(node.Id);
        MarkKeyDirty(parentContainerId);
        return node;
    }

    /// <summary>
    /// Moves a logic or container node out of <paramref name="fromContainerId"/> and into
    /// <paramref name="toContainerId"/>: drops it from the old parent's child list (and any connections in the old
    /// container that touched its boundary ports), adds it to the new parent, repoints its <c>ParentContainer</c>,
    /// and places it at (<paramref name="x"/>, <paramref name="y"/>) on the new container's canvas. No-op when the
    /// two containers are the same, either container is unknown, or the node is neither a logic nor a container node.
    /// </summary>
    public void ReparentNode(Guid fromContainerId, Guid nodeId, Guid toContainerId, double x, double y)
    {
        using var _ = Edit(); // node + both containers change together
        if (fromContainerId == toContainerId) return;
        if (!CurrentProject!.ContainerNodes.TryGetValue(fromContainerId, out StoryContainerNode? from)) return;
        if (!CurrentProject.ContainerNodes.TryGetValue(toContainerId, out StoryContainerNode? to)) return;

        if (CurrentProject.LogicNodes.TryGetValue(nodeId, out StoryLogicNode? logic))
        {
            List<Guid> boundaryPoints = logic.ExitMode == StoryLogicExitMode.SinglePath
                ? [logic.EntryPoint.Id, logic.VariablesIn.Id, logic.VFlowOut.Id]
                : [logic.EntryPoint.Id, logic.VariablesIn.Id, .. logic.Choices.Select(c => c.OuterFlowOut.Id)];
            from.Logic.Remove(nodeId);
            to.Logic.Add(nodeId);
            logic.ParentContainer = toContainerId;
            logic.X               = x;
            logic.Y               = y;
            RemoveConnectionsFor(fromContainerId, boundaryPoints);
        }
        else if (CurrentProject.ContainerNodes.TryGetValue(nodeId, out StoryContainerNode? child))
        {
            List<Guid> boundaryPoints = [.. child.EntryPoints.Select(p => p.Id), .. child.ExitPoints.Select(p => p.Id)];
            from.Containers.Remove(nodeId);
            to.Containers.Add(nodeId);
            child.ParentContainer = toContainerId;
            child.X               = x;
            child.Y               = y;
            RemoveConnectionsFor(fromContainerId, boundaryPoints);
        }
        else
        {
            return;
        }

        MarkKeyDirty(nodeId);
        MarkKeyDirty(fromContainerId);
        MarkKeyDirty(toContainerId);
    }

    /// <summary>
    /// Adds a new portal "pair" (orange) to <paramref name="parentContainerId"/>: one <b>portal in</b> placed at
    /// (<paramref name="x"/>, <paramref name="y"/>) and its paired <b>portal out</b> offset to the right. Flow that
    /// reaches any portal in teleports to the portal out. Marks it dirty and returns the created portal.
    /// </summary>
    public StoryPortalNode AddPortalNode(
        Guid   parentContainerId,
        string name,
        string description,
        double x,
        double y)
    {
        using var _ = Edit(); // portal + parent container change together
        StoryPortalNode portal = new()
        {
            Name            = name,
            Description     = description,
            ParentContainer = parentContainerId,
            OutPoint        = new StoryConnectionPoint { Name = "Out", X = x + _PORTAL_PAIR_GAP, Y = y }
        };
        portal.InPoints.Add(new StoryConnectionPoint { Name = "In", X = x, Y = y });

        CurrentProject!.PortalNodes.Add(portal.Id, portal);

        if (CurrentProject.ContainerNodes.TryGetValue(parentContainerId, out StoryContainerNode? parent))
            parent.Portals.Add(portal.Id);

        MarkKeyDirty(portal.Id);
        MarkKeyDirty(parentContainerId);
        return portal;
    }

    /// <summary>
    /// Adds another <b>portal in</b> to the pair that <paramref name="pointId"/> belongs to (either an existing in
    /// point or the out point). The new in is stacked below the lowest existing in point. Returns the portal, or
    /// null when the point does not belong to any portal.
    /// </summary>
    public StoryPortalNode? AddPortalIn(Guid pointId)
    {
        StoryPortalNode? portal = FindPortalByPoint(pointId);
        if (portal is null) return null;

        double x = portal.InPoints.Count > 0 ? portal.InPoints.Min(p => p.X)     : portal.OutPoint.X - _PORTAL_PAIR_GAP;
        double y = portal.InPoints.Count > 0 ? portal.InPoints.Max(p => p.Y) + 90 : portal.OutPoint.Y;

        portal.InPoints.Add(new StoryConnectionPoint { Name = "In", X = x, Y = y });
        MarkKeyDirty(portal.Id);
        return portal;
    }

    /// <summary>Renames / re-describes a portal pair.</summary>
    public void UpdatePortalNode(Guid portalId, string name, string description)
    {
        if (!CurrentProject!.PortalNodes.TryGetValue(portalId, out StoryPortalNode? portal)) return;
        portal.Name        = name;
        portal.Description = description;
        MarkKeyDirty(portalId);
    }

    /// <summary>The portal pair that owns <paramref name="pointId"/> (its out point or any in point), or null.</summary>
    public StoryPortalNode? FindPortalByPoint(Guid pointId) =>
        CurrentProject?.PortalNodes.Values.FirstOrDefault(
            p => p.OutPoint.Id == pointId || p.InPoints.Exists(ip => ip.Id == pointId));

    private const double _PORTAL_PAIR_GAP = 320;

    // ── Logic node inner content graph (localization / icon nodes + wiring) ────

    /// <summary>Places a logic node's Entry and Exit nodes on its inner canvas so they don't overlap.</summary>
    private static void LayoutLogicInnerPoints(StoryLogicNode logic)
    {
        logic.EntryPoint.X  = 60;
        logic.EntryPoint.Y  = 200;
        logic.ExitLFlowIn.X = 660;
        logic.ExitLFlowIn.Y = 200;
        logic.PrevExitVariable.X = 60;
        logic.PrevExitVariable.Y = 360;
    }

    /// <summary>Adds a Localization node (referencing <paramref name="keyId"/>) to a logic node's inner graph.</summary>
    public StoryLocalizationNode? AddLocalizationNode(Guid logicId, Guid keyId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StoryLocalizationNode node = new() { SelectedKeyId = keyId, X = x, Y = y };
        logic.LocalizationNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>Adds an Icon node (referencing image <paramref name="imageId"/>) to a logic node's inner graph.</summary>
    public StoryIconNode? AddIconNode(Guid logicId, Guid imageId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StoryIconNode node = new() { SelectedImageId = imageId, X = x, Y = y };
        logic.IconNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>Changes the localization key a Localization node points at.</summary>
    public void UpdateLocalizationNode(Guid logicId, Guid nodeId, Guid keyId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryLocalizationNode? node = logic.LocalizationNodes.Find(n => n.Id == nodeId);
        if (node is null) return;
        node.SelectedKeyId = keyId;
        MarkKeyDirty(logicId);
    }

    /// <summary>Changes the icon an Icon node points at.</summary>
    public void UpdateIconNode(Guid logicId, Guid nodeId, Guid imageId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryIconNode? node = logic.IconNodes.Find(n => n.Id == nodeId);
        if (node is null) return;
        node.SelectedImageId = imageId;
        MarkKeyDirty(logicId);
    }

    /// <summary>Deletes a Localization node and any inner wire that touched its output.</summary>
    public void DeleteLocalizationNode(Guid logicId, Guid nodeId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryLocalizationNode? node = logic.LocalizationNodes.Find(n => n.Id == nodeId);
        if (node is null) return;

        logic.LocalizationNodes.Remove(node);
        logic.ContentConnections.RemoveAll(c => c.FromPoint == node.OutPoint.Id || c.ToPoint == node.OutPoint.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>Deletes an Icon node and any inner wire that touched its output.</summary>
    public void DeleteIconNode(Guid logicId, Guid nodeId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryIconNode? node = logic.IconNodes.Find(n => n.Id == nodeId);
        if (node is null) return;

        logic.IconNodes.Remove(node);
        logic.ContentConnections.RemoveAll(c => c.FromPoint == node.OutPoint.Id || c.ToPoint == node.OutPoint.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>Adds a Light/Dark switch node (picks an icon by theme) to a logic node's inner graph.</summary>
    public StoryLightDarkSwitchNode? AddLightDarkSwitchNode(Guid logicId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StoryLightDarkSwitchNode node = new() { X = x, Y = y };
        logic.LightDarkSwitchNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>Deletes a Light/Dark switch node and any inner wire that touched its ports.</summary>
    public void DeleteLightDarkSwitchNode(Guid logicId, Guid nodeId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryLightDarkSwitchNode? node = logic.LightDarkSwitchNodes.Find(n => n.Id == nodeId);
        if (node is null) return;

        logic.LightDarkSwitchNodes.Remove(node);
        logic.ContentConnections.RemoveAll(c =>
            c.FromPoint == node.OutPoint.Id || c.ToPoint == node.OutPoint.Id ||
            c.ToPoint   == node.DarkIn.Id   || c.ToPoint == node.LightIn.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>Adds a SmartFormat node (formats a text with connected variable values) to a logic node's inner graph.</summary>
    public StorySmartFormatNode? AddSmartFormatNode(Guid logicId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StorySmartFormatNode node = new() { X = x, Y = y };
        logic.SmartFormatNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>Deletes a SmartFormat node and any inner wire that touched its ports.</summary>
    public void DeleteSmartFormatNode(Guid logicId, Guid nodeId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StorySmartFormatNode? node = logic.SmartFormatNodes.Find(n => n.Id == nodeId);
        if (node is null) return;

        logic.SmartFormatNodes.Remove(node);
        logic.ContentConnections.RemoveAll(c =>
            c.FromPoint == node.OutPoint.Id     || c.ToPoint == node.OutPoint.Id ||
            c.ToPoint   == node.LocalizationIn.Id || c.ToPoint == node.VariablesIn.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>Adds an External Variable node (referencing <paramref name="variableId"/>) to a logic node's inner graph.</summary>
    public StoryExternalVariableNode? AddExternalVariableNode(Guid logicId, Guid variableId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StoryExternalVariableNode node = new() { SelectedVariableId = variableId, X = x, Y = y };
        logic.ExternalVariableNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>Changes the story variable an External Variable node points at.</summary>
    public void UpdateExternalVariableNode(Guid logicId, Guid nodeId, Guid variableId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryExternalVariableNode? node = logic.ExternalVariableNodes.Find(n => n.Id == nodeId);
        if (node is null) return;
        node.SelectedVariableId = variableId;
        MarkKeyDirty(logicId);
    }

    /// <summary>Deletes an External Variable node and any inner wire that touched its output.</summary>
    public void DeleteExternalVariableNode(Guid logicId, Guid nodeId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryExternalVariableNode? node = logic.ExternalVariableNodes.Find(n => n.Id == nodeId);
        if (node is null) return;

        logic.ExternalVariableNodes.Remove(node);
        logic.ContentConnections.RemoveAll(c => c.FromPoint == node.OutPoint.Id || c.ToPoint == node.OutPoint.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>Adds a Get Variable node (reads registered variable <paramref name="registeredVariableId"/>) to a logic node's inner graph.</summary>
    public StoryGetVariableNode? AddGetVariableNode(Guid logicId, Guid registeredVariableId, string nameOverride, string previewValue, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StoryGetVariableNode node = new()
        {
            RegisteredVariableId = registeredVariableId,
            NameOverride         = nameOverride,
            PreviewValue         = previewValue,
            X                    = x,
            Y                    = y
        };
        logic.GetVariableNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>Updates the registered variable, name override and preview value a Get Variable node reads.</summary>
    public void UpdateGetVariableNode(Guid logicId, Guid nodeId, Guid registeredVariableId, string nameOverride, string previewValue)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryGetVariableNode? node = logic.GetVariableNodes.Find(n => n.Id == nodeId);
        if (node is null) return;
        node.RegisteredVariableId = registeredVariableId;
        node.NameOverride         = nameOverride;
        node.PreviewValue         = previewValue;
        MarkKeyDirty(logicId);
    }

    /// <summary>Deletes a Get Variable node and any inner wire that touched its output.</summary>
    public void DeleteGetVariableNode(Guid logicId, Guid nodeId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryGetVariableNode? node = logic.GetVariableNodes.Find(n => n.Id == nodeId);
        if (node is null) return;

        logic.GetVariableNodes.Remove(node);
        logic.ContentConnections.RemoveAll(c =>
            c.FromPoint == node.OutPoint.Id || c.ToPoint == node.OutPoint.Id ||
            c.FromPoint == node.SlotOutPoint.Id || c.ToPoint == node.SlotOutPoint.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>Adds a Constant Variable node (a named constant value) to a logic node's inner graph.</summary>
    public StoryConstantVariableNode? AddConstantVariableNode(Guid logicId, string name, string value, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StoryConstantVariableNode node = new() { Name = name, Value = value, X = x, Y = y };
        logic.ConstantVariableNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>Updates the name and value of a Constant Variable node.</summary>
    public void UpdateConstantVariableNode(Guid logicId, Guid nodeId, string name, string value)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryConstantVariableNode? node = logic.ConstantVariableNodes.Find(n => n.Id == nodeId);
        if (node is null) return;
        node.Name  = name;
        node.Value = value;
        MarkKeyDirty(logicId);
    }

    /// <summary>Deletes a Constant Variable node and any inner wire that touched its output.</summary>
    public void DeleteConstantVariableNode(Guid logicId, Guid nodeId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryConstantVariableNode? node = logic.ConstantVariableNodes.Find(n => n.Id == nodeId);
        if (node is null) return;

        logic.ConstantVariableNodes.Remove(node);
        logic.ContentConnections.RemoveAll(c => c.FromPoint == node.OutPoint.Id || c.ToPoint == node.OutPoint.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>Adds a FlowText node (renders a text block on the flow spine) to a logic node's inner graph.</summary>
    public StoryFlowTextNode? AddFlowTextNode(Guid logicId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StoryFlowTextNode node = new() { X = x, Y = y };
        logic.FlowTextNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>Updates a FlowText node's per-medium render flags (whether its text renders in the App / Gamebook) and its frame style.</summary>
    public void UpdateFlowTextNode(Guid logicId, Guid nodeId, bool renderInApp, bool renderInGamebook, StoryTextFrameStyle frameStyle)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryFlowTextNode? node = logic.FlowTextNodes.Find(n => n.Id == nodeId);
        if (node is null) return;

        node.RenderInApp      = renderInApp;
        node.RenderInGamebook = renderInGamebook;
        node.FrameStyle       = frameStyle;
        MarkKeyDirty(logicId);
    }

    /// <summary>Deletes a FlowText node and any inner wire that touched its flow or text ports.</summary>
    public void DeleteFlowTextNode(Guid logicId, Guid nodeId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryFlowTextNode? node = logic.FlowTextNodes.Find(n => n.Id == nodeId);
        if (node is null) return;

        logic.FlowTextNodes.Remove(node);
        logic.ContentConnections.RemoveAll(c =>
            c.FromPoint == node.FlowOut.Id || c.ToPoint == node.FlowOut.Id ||
            c.FromPoint == node.FlowIn.Id  || c.ToPoint == node.FlowIn.Id  ||
            c.ToPoint   == node.TextIn.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>Adds a Split-for-App node (breaks the App render into a new "continue" page) to a logic node's inner graph.</summary>
    public StorySplitForAppNode? AddSplitForAppNode(Guid logicId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StorySplitForAppNode node = new() { X = x, Y = y };
        logic.SplitForAppNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>Deletes a Split-for-App node and any inner wire that touched its flow ports.</summary>
    public void DeleteSplitForAppNode(Guid logicId, Guid nodeId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StorySplitForAppNode? node = logic.SplitForAppNodes.Find(n => n.Id == nodeId);
        if (node is null) return;

        logic.SplitForAppNodes.Remove(node);
        logic.ContentConnections.RemoveAll(c =>
            c.FromPoint == node.FlowOut.Id || c.ToPoint == node.FlowOut.Id ||
            c.FromPoint == node.FlowIn.Id  || c.ToPoint == node.FlowIn.Id);
        MarkKeyDirty(logicId);
    }

    // ── Logic portals (inner-graph value relays: one-in / many-out) ──────────

    /// <summary>
    /// Adds a logic portal (a one-in / many-out value relay) to a logic node's inner graph: one <b>portal in</b> at
    /// (<paramref name="x"/>, <paramref name="y"/>) and one paired <b>portal out</b> offset to the right. The in accepts
    /// any value signal; each out adopts its type once connected. Returns the created portal, or null when unknown.
    /// </summary>
    public StoryLogicPortalNode? AddLogicPortalNode(Guid logicId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StoryLogicPortalNode portal = new()
        {
            Name    = $"Portal {logic.LogicPortalNodes.Count + 1}",
            InPoint = new StoryConnectionPoint { Name = "In", X = x, Y = y }
        };
        portal.OutPoints.Add(new StoryConnectionPoint { Name = "Out", X = x + _PORTAL_PAIR_GAP, Y = y });

        logic.LogicPortalNodes.Add(portal);
        MarkKeyDirty(logicId);
        return portal;
    }

    /// <summary>
    /// Renames / re-describes the logic portal that owns <paramref name="pointId"/> (its in or any out). The name and
    /// description are shared by the whole pair, so editing either the in or an out updates them all.
    /// </summary>
    public void UpdateLogicPortalNode(Guid logicId, Guid pointId, string name, string description)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryLogicPortalNode? portal = FindLogicPortalByPoint(logic, pointId);
        if (portal is null) return;

        portal.Name        = name;
        portal.Description = description;
        MarkKeyDirty(logicId);
    }

    /// <summary>
    /// Adds another <b>portal out</b> to the logic portal that <paramref name="pointId"/> belongs to (its in or any
    /// out). The new out is stacked below the lowest existing out. Returns the portal, or null when the point does not
    /// belong to any logic portal in <paramref name="logicId"/>.
    /// </summary>
    public StoryLogicPortalNode? AddLogicPortalOut(Guid logicId, Guid pointId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;
        StoryLogicPortalNode? portal = FindLogicPortalByPoint(logic, pointId);
        if (portal is null) return null;

        double x = portal.OutPoints.Count > 0 ? portal.OutPoints.Min(p => p.X)      : portal.InPoint.X + _PORTAL_PAIR_GAP;
        double y = portal.OutPoints.Count > 0 ? portal.OutPoints.Max(p => p.Y) + 90 : portal.InPoint.Y;

        portal.OutPoints.Add(new StoryConnectionPoint { Name = "Out", X = x, Y = y });
        MarkKeyDirty(logicId);
        return portal;
    }

    /// <summary>
    /// Deletes a single logic portal <b>out</b> point (identified by <paramref name="outPointId"/>). Deleting the
    /// portal's last out deletes the whole portal. Prunes inner wires that touch the removed point(s).
    /// </summary>
    public void DeleteLogicPortalOut(Guid logicId, Guid outPointId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryLogicPortalNode? portal = FindLogicPortalByPoint(logic, outPointId);
        if (portal is null || !portal.OutPoints.Exists(p => p.Id == outPointId)) return;

        if (portal.OutPoints.Count <= 1)
        {
            DeleteLogicPortal(logicId, portal.Id);
            return;
        }

        portal.OutPoints.RemoveAll(p => p.Id == outPointId);
        logic.ContentConnections.RemoveAll(c => c.FromPoint == outPointId || c.ToPoint == outPointId);
        MarkKeyDirty(logicId);
    }

    /// <summary>Deletes a whole logic portal (its in point and every out point) and any inner wire that touched them.</summary>
    public void DeleteLogicPortal(Guid logicId, Guid portalId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryLogicPortalNode? portal = logic.LogicPortalNodes.Find(p => p.Id == portalId);
        if (portal is null) return;

        HashSet<Guid> points = new(portal.OutPoints.Select(p => p.Id)) { portal.InPoint.Id };
        logic.LogicPortalNodes.Remove(portal);
        logic.ContentConnections.RemoveAll(c => points.Contains(c.FromPoint) || points.Contains(c.ToPoint));
        MarkKeyDirty(logicId);
    }

    /// <summary>Deletes the whole logic portal that owns <paramref name="pointId"/> (used when its in point is deleted).</summary>
    public void DeleteLogicPortalByPoint(Guid logicId, Guid pointId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        if (FindLogicPortalByPoint(logic, pointId) is StoryLogicPortalNode portal)
            DeleteLogicPortal(logicId, portal.Id);
    }

    /// <summary>The logic portal in <paramref name="logic"/> that owns <paramref name="pointId"/> (its in or any out), or null.</summary>
    public StoryLogicPortalNode? FindLogicPortalByPoint(StoryLogicNode logic, Guid pointId) =>
        logic.LogicPortalNodes.Find(p => p.InPoint.Id == pointId || p.OutPoints.Exists(o => o.Id == pointId));

    // ── Condition-flow pairs (inner-graph optional flow blocks) ──────────────

    /// <summary>
    /// Adds a condition-flow pair to a logic node's inner graph: a <b>Condition</b> card at
    /// (<paramref name="x"/>, <paramref name="y"/>) and its paired <b>End condition</b> card offset to the right. The
    /// Condition card sits on the flow spine and injects the block wired from its "condition true" output (up to the
    /// End card) whenever its condition holds. Returns the created pair, or null when the logic node is unknown.
    /// </summary>
    public StoryConditionFlowNode? AddConditionFlowNode(Guid logicId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StoryConditionFlowNode node = new()
        {
            Name = $"Condition {logic.ConditionFlowNodes.Count + 1}",
            X    = x,
            Y    = y,
            EndX = x + _PORTAL_PAIR_GAP,
            EndY = y
        };
        logic.ConditionFlowNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>Updates a condition-flow node's name, condition expression and negate flag (its point ids are preserved so wires survive).</summary>
    public void UpdateConditionFlowNode(Guid logicId, Guid nodeId, string name, StoryConditionExpr condition, bool negate)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryConditionFlowNode? node = logic.ConditionFlowNodes.Find(n => n.Id == nodeId);
        if (node is null) return;
        node.Name      = name;
        node.Condition = condition;
        node.Negate    = negate;
        MarkKeyDirty(logicId);
    }

    /// <summary>
    /// Deletes a whole condition-flow pair (identified by either card's id — its <see cref="StoryConditionFlowNode.Id"/>
    /// or <see cref="StoryConditionFlowNode.EndId"/>) and every inner wire that touched any of its five ports.
    /// </summary>
    public void DeleteConditionFlowNode(Guid logicId, Guid anyId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryConditionFlowNode? node = FindConditionFlowByAnyId(logic, anyId);
        if (node is null) return;

        HashSet<Guid> points = new()
        {
            node.FlowIn.Id, node.VariablesIn.Id, node.ContinueOut.Id, node.ConditionTrueOut.Id, node.EndFlowIn.Id
        };
        logic.ConditionFlowNodes.Remove(node);
        logic.ContentConnections.RemoveAll(c => points.Contains(c.FromPoint) || points.Contains(c.ToPoint));
        MarkKeyDirty(logicId);
    }

    /// <summary>The condition-flow pair in <paramref name="logic"/> whose Condition or End card carries <paramref name="anyId"/>, or null.</summary>
    public StoryConditionFlowNode? FindConditionFlowByAnyId(StoryLogicNode logic, Guid anyId) =>
        logic.ConditionFlowNodes.Find(n => n.Id == anyId || n.EndId == anyId);

    // ── Comment notes ──────────────────────────────────────────────────────
    // A comment lives either in a container's graph or in a logic node's inner graph; the owner id resolves to
    // whichever exists. Comments have no ports, so there are never wires to clean up on delete.

    /// <summary>Adds a free-text comment note to <paramref name="ownerId"/>'s graph (a container or a logic node).</summary>
    public StoryCommentNode? AddCommentNode(Guid ownerId, string text, double x, double y)
    {
        StoryCommentNode node = new() { Text = text, X = x, Y = y };
        if (CurrentProject!.LogicNodes.TryGetValue(ownerId, out StoryLogicNode? logic))
            logic.CommentNodes.Add(node);
        else if (CurrentProject.ContainerNodes.TryGetValue(ownerId, out StoryContainerNode? container))
            container.Comments.Add(node);
        else
            return null;
        MarkKeyDirty(ownerId);
        return node;
    }

    /// <summary>Updates a comment note's text.</summary>
    public void UpdateCommentNode(Guid ownerId, Guid nodeId, string text)
    {
        if (FindCommentNode(ownerId, nodeId) is not StoryCommentNode node) return;
        node.Text = text;
        MarkKeyDirty(ownerId);
    }

    /// <summary>Moves a comment note to a new canvas position.</summary>
    public void MoveCommentNode(Guid ownerId, Guid nodeId, double x, double y)
    {
        if (FindCommentNode(ownerId, nodeId) is not StoryCommentNode node) return;
        node.X = x;
        node.Y = y;
        MarkKeyDirty(ownerId);
    }

    /// <summary>Deletes a comment note from <paramref name="ownerId"/>'s graph.</summary>
    public void DeleteCommentNode(Guid ownerId, Guid nodeId)
    {
        if (CurrentProject!.LogicNodes.TryGetValue(ownerId, out StoryLogicNode? logic))
            logic.CommentNodes.RemoveAll(n => n.Id == nodeId);
        else if (CurrentProject.ContainerNodes.TryGetValue(ownerId, out StoryContainerNode? container))
            container.Comments.RemoveAll(n => n.Id == nodeId);
        else
            return;
        MarkKeyDirty(ownerId);
    }

    /// <summary>Finds a comment note by id in <paramref name="ownerId"/>'s container graph or logic inner graph.</summary>
    private StoryCommentNode? FindCommentNode(Guid ownerId, Guid nodeId)
    {
        if (CurrentProject!.LogicNodes.TryGetValue(ownerId, out StoryLogicNode? logic))
            return logic.CommentNodes.Find(n => n.Id == nodeId);
        if (CurrentProject.ContainerNodes.TryGetValue(ownerId, out StoryContainerNode? container))
            return container.Comments.Find(n => n.Id == nodeId);
        return null;
    }

    /// <summary>Adds a Register-variable node (claims a storage slot for a new variable) to a logic node's flow spine.</summary>
    public StoryRegisterVariableNode? AddRegisterVariableNode(Guid logicId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StoryRegisterVariableNode node = new() { X = x, Y = y };
        logic.RegisterVariableNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>Updates a Register-variable node's definition (identity, slot and value parameters).</summary>
    public void UpdateRegisterVariableNode(
        Guid logicId, Guid nodeId, string name, string description, StorageVariableType type, int slotIndex,
        NumberStorageMode mode, NumberValueCount valueCount, bool secret, NumberAssignment assignment,
        int specificValue, Guid conditionKeyId, StorageInstructionPlacement placement, StringValueMode stringMode,
        string stringValue, StringInputKind stringInputKind, string previewValue)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryRegisterVariableNode? node = logic.RegisterVariableNodes.Find(n => n.Id == nodeId);
        if (node is null) return;

        node.Name            = name;
        node.Description     = description;
        node.Type            = type;
        node.SlotIndex       = slotIndex;
        node.Mode            = mode;
        node.ValueCount      = valueCount;
        node.Secret          = secret;
        node.Assignment      = assignment;
        node.SpecificValue   = specificValue;
        node.ConditionKeyId  = conditionKeyId;
        node.Placement       = placement;
        node.StringMode      = stringMode;
        node.StringValue     = stringValue;
        node.StringInputKind = stringInputKind;
        node.PreviewValue    = previewValue;

        // The Instruction / Placeholder ports only exist for a player-input String — drop any stale wire when they no longer apply.
        if (type != StorageVariableType.String || stringMode != StringValueMode.PlayerInput)
            logic.ContentConnections.RemoveAll(c => c.ToPoint == node.InstructionIn.Id || c.ToPoint == node.PlaceholderIn.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>Deletes a Register-variable node and any inner wire that touched its flow ports.</summary>
    public void DeleteRegisterVariableNode(Guid logicId, Guid nodeId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryRegisterVariableNode? node = logic.RegisterVariableNodes.Find(n => n.Id == nodeId);
        if (node is null) return;

        logic.RegisterVariableNodes.Remove(node);
        logic.ContentConnections.RemoveAll(c =>
            c.FromPoint == node.FlowOut.Id || c.ToPoint == node.FlowOut.Id ||
            c.FromPoint == node.FlowIn.Id  || c.ToPoint == node.FlowIn.Id  ||
            c.ToPoint   == node.InstructionIn.Id || c.ToPoint == node.PlaceholderIn.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>Adds a Set-variable node (assigns a value to an already-registered variable) to a logic node's flow spine.</summary>
    public StorySetVariableNode? AddSetVariableNode(Guid logicId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StorySetVariableNode node = new() { X = x, Y = y };
        logic.SetVariableNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>Updates a Set-variable node's target and value assignment.</summary>
    public void UpdateSetVariableNode(
        Guid logicId, Guid nodeId, Guid registeredVariableId, NumberAssignment assignment, bool secret, int specificValue,
        StorageInstructionPlacement placement, StringValueMode stringMode, string stringValue, StringInputKind stringInputKind)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StorySetVariableNode? node = logic.SetVariableNodes.Find(n => n.Id == nodeId);
        if (node is null) return;

        node.RegisteredVariableId = registeredVariableId;
        node.Assignment           = assignment;
        node.Secret               = secret;
        node.SpecificValue        = specificValue;
        node.Placement            = placement;
        node.StringMode           = stringMode;
        node.StringValue          = stringValue;
        node.StringInputKind      = stringInputKind;

        // The Instruction / Placeholder ports only exist for a player-input String target — drop any stale wire when they no longer apply.
        StoryRegisterVariableNode? target = EditorProjection.FindRegister(CurrentProject!, registeredVariableId);
        if (target is not { Type: StorageVariableType.String } || stringMode != StringValueMode.PlayerInput)
            logic.ContentConnections.RemoveAll(c => c.ToPoint == node.InstructionIn.Id || c.ToPoint == node.PlaceholderIn.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>Deletes a Set-variable node and any inner wire that touched its flow ports.</summary>
    public void DeleteSetVariableNode(Guid logicId, Guid nodeId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StorySetVariableNode? node = logic.SetVariableNodes.Find(n => n.Id == nodeId);
        if (node is null) return;

        logic.SetVariableNodes.Remove(node);
        logic.ContentConnections.RemoveAll(c =>
            c.FromPoint == node.FlowOut.Id || c.ToPoint == node.FlowOut.Id ||
            c.FromPoint == node.FlowIn.Id  || c.ToPoint == node.FlowIn.Id  ||
            c.ToPoint   == node.InstructionIn.Id || c.ToPoint == node.PlaceholderIn.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>Adds an Unregister-variable node (releases a registered variable, freeing its slot) to a logic node's flow spine.</summary>
    public StoryUnregisterVariableNode? AddUnregisterVariableNode(Guid logicId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StoryUnregisterVariableNode node = new() { X = x, Y = y };
        logic.UnregisterVariableNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>Updates which registered variable an Unregister-variable node releases, and where its instruction sits.</summary>
    public void UpdateUnregisterVariableNode(Guid logicId, Guid nodeId, Guid registeredVariableId, StorageInstructionPlacement placement)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryUnregisterVariableNode? node = logic.UnregisterVariableNodes.Find(n => n.Id == nodeId);
        if (node is null) return;

        node.RegisteredVariableId = registeredVariableId;
        node.Placement            = placement;
        MarkKeyDirty(logicId);
    }

    /// <summary>Deletes an Unregister-variable node and any inner wire that touched its flow ports.</summary>
    public void DeleteUnregisterVariableNode(Guid logicId, Guid nodeId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StoryUnregisterVariableNode? node = logic.UnregisterVariableNodes.Find(n => n.Id == nodeId);
        if (node is null) return;

        logic.UnregisterVariableNodes.Remove(node);
        logic.ContentConnections.RemoveAll(c =>
            c.FromPoint == node.FlowOut.Id || c.ToPoint == node.FlowOut.Id ||
            c.FromPoint == node.FlowIn.Id  || c.ToPoint == node.FlowIn.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>Adds a Set-external-variable node (assigns a value to a story-wide external variable) to a logic node's flow spine.</summary>
    public StorySetExternalVariableNode? AddSetExternalVariableNode(Guid logicId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StorySetExternalVariableNode node = new() { X = x, Y = y };
        logic.SetExternalVariableNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>
    /// Updates which external variable a Set-external-variable node assigns, the mode it uses, and — in
    /// <see cref="StorySetExternalVariableMode.SpecificValue"/> mode — the fixed value it assigns, or in
    /// <see cref="StorySetExternalVariableMode.RemapFromVariable"/> mode the incoming-value conversion table.
    /// Leaving the map/remap modes (which both consume the value input) drops any wire feeding it.
    /// </summary>
    public void UpdateSetExternalVariableNode(
        Guid logicId, Guid nodeId, Guid selectedVariableId, StorySetExternalVariableMode mode, string value,
        List<StorySetExternalVariableRemap> valueMap)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StorySetExternalVariableNode? node = logic.SetExternalVariableNodes.Find(n => n.Id == nodeId);
        if (node is null) return;

        node.SelectedVariableId = selectedVariableId;
        node.Mode               = mode;
        node.Value              = value;
        node.ValueMap           = valueMap;
        bool consumesInput = mode is StorySetExternalVariableMode.MapFromVariable or StorySetExternalVariableMode.RemapFromVariable;
        if (!consumesInput)
            logic.ContentConnections.RemoveAll(c => c.FromPoint == node.ValueIn.Id || c.ToPoint == node.ValueIn.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>Deletes a Set-external-variable node and any inner wire that touched its flow or value ports.</summary>
    public void DeleteSetExternalVariableNode(Guid logicId, Guid nodeId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        StorySetExternalVariableNode? node = logic.SetExternalVariableNodes.Find(n => n.Id == nodeId);
        if (node is null) return;

        logic.SetExternalVariableNodes.Remove(node);
        logic.ContentConnections.RemoveAll(c =>
            c.FromPoint == node.FlowOut.Id || c.ToPoint == node.FlowOut.Id ||
            c.FromPoint == node.FlowIn.Id  || c.ToPoint == node.FlowIn.Id  ||
            c.FromPoint == node.ValueIn.Id || c.ToPoint == node.ValueIn.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>
    /// Reconciles a logic node's Exit-node <paramref name="choices"/>: a choice with a matching id keeps its ports
    /// (and inner Text / outer Flow wires) and takes the new name / Else flag / condition / declared-variable values;
    /// new choices (empty id) are created; removed choices have their inner Text wire and outer Flow wire pruned.
    /// Order follows <paramref name="choices"/>.
    /// </summary>
    public void UpdateExitNode(
        Guid logicId,
        List<(Guid Id, string Name, bool IsElse, StoryConditionExpr? Condition, List<StoryChoiceVarValue> Values)> choices)
    {
        using var _ = Edit(); // choices + the parent container's outer wires change together
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;

        HashSet<Guid>     kept    = new();
        List<StoryChoice> rebuilt = new();
        foreach ((Guid id, string name, bool isElse, StoryConditionExpr? condition, List<StoryChoiceVarValue> values) in choices)
        {
            StoryChoice choice = (id != Guid.Empty ? logic.Choices.Find(c => c.Id == id) : null) ?? new StoryChoice();
            choice.Name           = name;
            choice.IsElse         = isElse;
            choice.Condition      = condition;
            choice.VariableValues = values;
            if (id != Guid.Empty) kept.Add(choice.Id);
            rebuilt.Add(choice);
        }

        // Prune removed choices' inner Text wires and outer Flow wires before swapping the list in.
        foreach (StoryChoice removed in logic.Choices)
            if (!kept.Contains(removed.Id))
            {
                logic.ContentConnections.RemoveAll(c => c.ToPoint == removed.TextIn.Id);
                RemoveConnectionsFor(logic.ParentContainer, [removed.OuterFlowOut.Id]);
            }

        logic.Choices.Clear();
        logic.Choices.AddRange(rebuilt);
        MarkKeyDirty(logicId);
        MarkKeyDirty(logic.ParentContainer);
    }

    /// <summary>Sets which registered storage variables are released when the story reaches The End (edited on the End node).</summary>
    public void SetEndUnregister(IEnumerable<Guid> registeredVariableIds)
    {
        if (CurrentProject is null) return;
        CurrentProject.Metadata.UnregisterAtEnd = registeredVariableIds.ToList();
        MarkDirty();
    }

    /// <summary>
    /// Wires an output (<paramref name="fromPoint"/>) to an input (<paramref name="toPoint"/>) inside a logic node's
    /// content graph. Only a flow output (LFlow / VFlow) leads to a single place, so any existing wire leaving
    /// <paramref name="fromPoint"/> is replaced. Every content output — Text (Localization / SmartFormat), Icon
    /// (Icon / LightDarkSwitch) and variable-supplying (External / Get / Constant / Prev Exit) — may fan out to
    /// many inputs and keeps them all. An input takes a single source (its previous wire is replaced) — except a
    /// SmartFormat / Exit-node <b>variables</b> input, which accepts many variable outputs and keeps them all.
    /// A no-op (returns null) if the exact wire already exists.
    /// </summary>
    public StoryConnection? ConnectContent(Guid logicId, Guid fromPoint, Guid toPoint)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;
        if (logic.ContentConnections.Exists(c => c.FromPoint == fromPoint && c.ToPoint == toPoint)) return null;

        // The variables inputs (SmartFormat, the Exit node's auto-resolution, a Condition node's operands) aggregate many variable outputs.
        bool multiInput  = logic.SmartFormatNodes.Exists(sf => sf.VariablesIn.Id == toPoint)
                        || logic.ExitVariablesIn.Id == toPoint
                        || logic.ConditionFlowNodes.Exists(cf => cf.VariablesIn.Id == toPoint);
        // Every non-flow content output is one-in-many-out — it keeps all its wires:
        //   Text  : Localization / SmartFormat
        //   Icon  : Icon / LightDarkSwitch
        //   Value : External / Get (value + slot) / Constant / Prev Exit
        // Only flow outputs (LFlow / VFlow) lead to a single place and get their previous wire replaced below.
        bool multiOutput = logic.LocalizationNodes.Exists(l => l.OutPoint.Id == fromPoint)
                        || logic.SmartFormatNodes.Exists(sf => sf.OutPoint.Id == fromPoint)
                        || logic.IconNodes.Exists(ic => ic.OutPoint.Id == fromPoint)
                        || logic.LightDarkSwitchNodes.Exists(ld => ld.OutPoint.Id == fromPoint)
                        || logic.ExternalVariableNodes.Exists(ev => ev.OutPoint.Id == fromPoint)
                        || logic.GetVariableNodes.Exists(gv => gv.OutPoint.Id == fromPoint || gv.SlotOutPoint.Id == fromPoint)
                        || logic.ConstantVariableNodes.Exists(cv => cv.OutPoint.Id == fromPoint)
                        || logic.LogicPortalNodes.Exists(p => p.OutPoints.Exists(o => o.Id == fromPoint))
                        || StorySelectionResolver.IncomingVariables(CurrentProject, logic).Exists(d => d.Id == fromPoint);
        logic.ContentConnections.RemoveAll(c => (!multiOutput && c.FromPoint == fromPoint) || (!multiInput && c.ToPoint == toPoint));

        StoryConnection connection = new() { FromPoint = fromPoint, ToPoint = toPoint };
        logic.ContentConnections.Add(connection);
        MarkKeyDirty(logicId);
        return connection;
    }

    /// <summary>Removes an inner content connection from a logic node.</summary>
    public void DisconnectContent(Guid logicId, Guid connectionId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        if (logic.ContentConnections.RemoveAll(c => c.Id == connectionId) > 0)
            MarkKeyDirty(logicId);
    }

    /// <summary>
    /// Persists a drag inside a logic node's inner graph. <paramref name="movedId"/> is the dragged EdNode id —
    /// the Entry point, an Exit point, or a Localization/Icon node — resolved to the right stored position.
    /// </summary>
    public void MoveLogicNode(Guid logicId, Guid movedId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;

        if (logic.EntryPoint.Id == movedId)
        {
            logic.EntryPoint.X = x;
            logic.EntryPoint.Y = y;
            MarkKeyDirty(logicId);
            return;
        }

        if (logic.ExitLFlowIn.Id == movedId)
        {
            logic.ExitLFlowIn.X = x;
            logic.ExitLFlowIn.Y = y;
            MarkKeyDirty(logicId);
            return;
        }

        if (logic.PrevExitVariable.Id == movedId)
        {
            logic.PrevExitVariable.X = x;
            logic.PrevExitVariable.Y = y;
            MarkKeyDirty(logicId);
            return;
        }

        StoryLocalizationNode? loc = logic.LocalizationNodes.Find(n => n.Id == movedId);
        if (loc is not null)
        {
            loc.X = x;
            loc.Y = y;
            MarkKeyDirty(logicId);
            return;
        }

        StoryIconNode? ico = logic.IconNodes.Find(n => n.Id == movedId);
        if (ico is not null)
        {
            ico.X = x;
            ico.Y = y;
            MarkKeyDirty(logicId);
            return;
        }

        StoryLightDarkSwitchNode? lds = logic.LightDarkSwitchNodes.Find(n => n.Id == movedId);
        if (lds is not null)
        {
            lds.X = x;
            lds.Y = y;
            MarkKeyDirty(logicId);
            return;
        }

        StorySmartFormatNode? sf = logic.SmartFormatNodes.Find(n => n.Id == movedId);
        if (sf is not null)
        {
            sf.X = x;
            sf.Y = y;
            MarkKeyDirty(logicId);
            return;
        }

        StoryExternalVariableNode? ev = logic.ExternalVariableNodes.Find(n => n.Id == movedId);
        if (ev is not null)
        {
            ev.X = x;
            ev.Y = y;
            MarkKeyDirty(logicId);
            return;
        }

        StoryGetVariableNode? gv = logic.GetVariableNodes.Find(n => n.Id == movedId);
        if (gv is not null)
        {
            gv.X = x;
            gv.Y = y;
            MarkKeyDirty(logicId);
            return;
        }

        StoryConstantVariableNode? cv = logic.ConstantVariableNodes.Find(n => n.Id == movedId);
        if (cv is not null)
        {
            cv.X = x;
            cv.Y = y;
            MarkKeyDirty(logicId);
            return;
        }

        StoryFlowTextNode? ft = logic.FlowTextNodes.Find(n => n.Id == movedId);
        if (ft is not null)
        {
            ft.X = x;
            ft.Y = y;
            MarkKeyDirty(logicId);
            return;
        }

        StorySplitForAppNode? split = logic.SplitForAppNodes.Find(n => n.Id == movedId);
        if (split is not null)
        {
            split.X = x;
            split.Y = y;
            MarkKeyDirty(logicId);
            return;
        }

        StoryRegisterVariableNode? reg = logic.RegisterVariableNodes.Find(n => n.Id == movedId);
        if (reg is not null)
        {
            reg.X = x;
            reg.Y = y;
            MarkKeyDirty(logicId);
            return;
        }

        StorySetVariableNode? set = logic.SetVariableNodes.Find(n => n.Id == movedId);
        if (set is not null)
        {
            set.X = x;
            set.Y = y;
            MarkKeyDirty(logicId);
            return;
        }

        StoryUnregisterVariableNode? unreg = logic.UnregisterVariableNodes.Find(n => n.Id == movedId);
        if (unreg is not null)
        {
            unreg.X = x;
            unreg.Y = y;
            MarkKeyDirty(logicId);
            return;
        }

        StorySetExternalVariableNode? setExt = logic.SetExternalVariableNodes.Find(n => n.Id == movedId);
        if (setExt is not null)
        {
            setExt.X = x;
            setExt.Y = y;
            MarkKeyDirty(logicId);
            return;
        }

        // A logic portal in/out node moved (its position lives on the portal's connection point).
        foreach (StoryLogicPortalNode portal in logic.LogicPortalNodes)
        {
            StoryConnectionPoint? point = portal.InPoint.Id == movedId
                ? portal.InPoint
                : portal.OutPoints.Find(p => p.Id == movedId);
            if (point is not null)
            {
                point.X = x;
                point.Y = y;
                MarkKeyDirty(logicId);
                return;
            }
        }

        // A condition-flow card moved — the Condition card lives on X/Y, the paired End card on EndX/EndY.
        StoryConditionFlowNode? cf = logic.ConditionFlowNodes.Find(n => n.Id == movedId || n.EndId == movedId);
        if (cf is not null)
        {
            if (cf.Id == movedId) { cf.X    = x; cf.Y    = y; }
            else                  { cf.EndX = x; cf.EndY = y; }
            MarkKeyDirty(logicId);
            return;
        }
    }

    // ── Images ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a PNG asset to the project's image library. <paramref name="name"/> must be unique across all images
    /// (both kinds) — it is how story text references the asset. <paramref name="base64"/> is the raw PNG bytes,
    /// base64-encoded. Marks the new image dirty and returns it.
    /// </summary>
    public StoryImage AddImage(string name, StoryImageKind kind, int width, int height, string base64)
    {
        StoryImage image = new()
        {
            Name   = name,
            Kind   = kind,
            Width  = width,
            Height = height,
            Data   = base64
        };

        CurrentProject!.Images.Add(image.Id, image);
        MarkKeyDirty(image.Id);
        return image;
    }

    /// <summary>Renames an image. No-op when the id is unknown.</summary>
    public void RenameImage(Guid imageId, string name)
    {
        if (!CurrentProject!.Images.TryGetValue(imageId, out StoryImage? image)) return;
        image.Name = name;
        MarkKeyDirty(imageId);
    }

    /// <summary>Deletes an image from the library. No-op when the id is unknown.</summary>
    public void DeleteImage(Guid imageId)
    {
        if (!CurrentProject!.Images.Remove(imageId)) return;
        MarkKeyDirty(imageId);
    }

    /// <summary>
    /// True when <paramref name="name"/> is already used by an image (case-insensitive). Pass the id being edited as
    /// <paramref name="ignoreId"/> so a rename to the same name doesn't collide with itself.
    /// </summary>
    public bool IsImageNameTaken(string name, Guid ignoreId = default) =>
        CurrentProject!.Images.Values.Any(
            i => i.Id != ignoreId && string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));

    // ── Variables ──────────────────────────────────────────────────────────────

    /// <summary>Adds a new story-wide variable with <paramref name="name"/> (and no values yet). Marks it dirty.</summary>
    public StoryVariable AddVariable(string name)
    {
        StoryVariable variable = new() { Name = name };
        CurrentProject!.Variables.Add(variable.Id, variable);
        MarkKeyDirty(variable.Id);
        return variable;
    }

    /// <summary>Applies an edit to a variable: name, description and possible values.</summary>
    public void UpdateVariable(
        Guid                id,
        string              name,
        string              description,
        IEnumerable<string> possibleValues)
    {
        if (!CurrentProject!.Variables.TryGetValue(id, out StoryVariable? variable)) return;
        variable.Name           = name;
        variable.Description    = description;
        variable.PossibleValues = possibleValues.ToList();
        MarkKeyDirty(id);
    }

    /// <summary>Deletes a variable from the project. No-op when the id is unknown.</summary>
    public void DeleteVariable(Guid id)
    {
        if (!CurrentProject!.Variables.Remove(id)) return;
        MarkKeyDirty(id);
    }

    /// <summary>
    /// True when <paramref name="name"/> is already used by another variable (case-insensitive). Pass the id being
    /// edited as <paramref name="ignoreId"/> so a rename to the same name doesn't collide with itself.
    /// </summary>
    public bool IsVariableNameTaken(string name, Guid ignoreId = default) =>
        StoryBuiltInVariables.IsReservedName(name) ||
        CurrentProject!.Variables.Values.Any(
            v => v.Id != ignoreId && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Wires an output point (<paramref name="fromPoint"/>) to an input point (<paramref name="toPoint"/>) inside
    /// <paramref name="containerId"/>. An exit/output can only lead to one place, so any existing connection leaving
    /// <paramref name="fromPoint"/> is replaced. A no-op (returns null) if the exact wire already exists.
    /// </summary>
    public StoryConnection? Connect(Guid containerId, Guid fromPoint, Guid toPoint)
    {
        if (!CurrentProject!.ContainerNodes.TryGetValue(containerId, out StoryContainerNode? container))
            return null;

        if (container.Connections.Exists(c => c.FromPoint == fromPoint && c.ToPoint == toPoint))
            return null;

        container.Connections.RemoveAll(c => c.FromPoint == fromPoint);

        StoryConnection connection = new() { FromPoint = fromPoint, ToPoint = toPoint };
        container.Connections.Add(connection);
        MarkKeyDirty(containerId);
        return connection;
    }

    /// <summary>Removes the connection with <paramref name="connectionId"/> from <paramref name="containerId"/>.</summary>
    public void Disconnect(Guid containerId, Guid connectionId)
    {
        if (!CurrentProject!.ContainerNodes.TryGetValue(containerId, out StoryContainerNode? container))
            return;

        if (container.Connections.RemoveAll(c => c.Id == connectionId) > 0)
            MarkKeyDirty(containerId);
    }

    // ── Delete / edit nodes ────────────────────────────────────────────────────

    /// <summary>
    /// Deletes a logic node from <paramref name="containerId"/>: drops it from the parent, removes its file and any
    /// connections in the container that touch its entry/exit ports.
    /// </summary>
    public void DeleteLogicNode(Guid containerId, Guid logicId)
    {
        using var _ = Edit(); // node removal + parent/connection cleanup are one step
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;

        List<Guid> pointIds = [logic.EntryPoint.Id, logic.VariablesIn.Id, logic.VFlowOut.Id, .. logic.Choices.Select(c => c.OuterFlowOut.Id)];

        CurrentProject.LogicNodes.Remove(logicId);
        if (CurrentProject.ContainerNodes.TryGetValue(containerId, out StoryContainerNode? parent))
            parent.Logic.Remove(logicId);

        RemoveConnectionsFor(containerId, pointIds);
        MarkKeyDirty(logicId);
        MarkKeyDirty(containerId);
    }

    /// <summary>
    /// Deletes a child container from <paramref name="parentContainerId"/> together with everything nested inside it
    /// (child containers, logic and portal nodes, recursively), and any connections that touch its boundary ports.
    /// </summary>
    public void DeleteContainerNode(Guid parentContainerId, Guid containerId)
    {
        using var _ = Edit(); // the whole nested subtree + connection cleanup are one step
        if (!CurrentProject!.ContainerNodes.TryGetValue(containerId, out StoryContainerNode? container)) return;

        List<Guid> boundaryPoints = [.. container.EntryPoints.Select(p => p.Id), .. container.ExitPoints.Select(p => p.Id)];

        DeleteContainerRecursive(container);

        if (CurrentProject.ContainerNodes.TryGetValue(parentContainerId, out StoryContainerNode? parent))
            parent.Containers.Remove(containerId);

        RemoveConnectionsFor(parentContainerId, boundaryPoints);
        MarkKeyDirty(parentContainerId);
    }

    /// <summary>Removes a container and all its descendants, marking every deleted file dirty so the save prunes it.</summary>
    private void DeleteContainerRecursive(StoryContainerNode container)
    {
        foreach (Guid childId in container.Containers.ToList())
        {
            if (CurrentProject!.ContainerNodes.TryGetValue(childId, out StoryContainerNode? child))
                DeleteContainerRecursive(child);
        }
        foreach (Guid logicId in container.Logic)
        {
            CurrentProject!.LogicNodes.Remove(logicId);
            MarkKeyDirty(logicId);
        }
        foreach (Guid portalId in container.Portals)
        {
            CurrentProject!.PortalNodes.Remove(portalId);
            MarkKeyDirty(portalId);
        }
        CurrentProject!.ContainerNodes.Remove(container.Id);
        MarkKeyDirty(container.Id);
    }

    /// <summary>
    /// Deletes a single portal <b>in</b> point (identified by <paramref name="inPointId"/>) from its pair. Deleting
    /// the pair's last in point deletes the whole portal. Cleans up connections that touch the removed point(s).
    /// </summary>
    public void DeletePortalIn(Guid containerId, Guid inPointId)
    {
        using var _ = Edit(); // in-point removal + connection cleanup (or whole-pair delete) are one step
        StoryPortalNode? portal = FindPortalByPoint(inPointId);
        if (portal is null) return;

        if (portal.InPoints.Count <= 1)
        {
            DeletePortal(containerId, portal.Id);
            return;
        }

        portal.InPoints.RemoveAll(p => p.Id == inPointId);
        RemoveConnectionsFor(containerId, [inPointId]);
        MarkKeyDirty(portal.Id);
    }

    /// <summary>Deletes a whole portal pair (its out point and every in point) from <paramref name="containerId"/>.</summary>
    public void DeletePortal(Guid containerId, Guid portalId)
    {
        using var _ = Edit(); // portal removal + parent/connection cleanup are one step
        if (!CurrentProject!.PortalNodes.TryGetValue(portalId, out StoryPortalNode? portal)) return;

        List<Guid> pointIds = [portal.OutPoint.Id, .. portal.InPoints.Select(p => p.Id)];

        CurrentProject.PortalNodes.Remove(portalId);
        if (CurrentProject.ContainerNodes.TryGetValue(containerId, out StoryContainerNode? parent))
            parent.Portals.Remove(portalId);

        RemoveConnectionsFor(containerId, pointIds);
        MarkKeyDirty(portalId);
        MarkKeyDirty(containerId);
    }

    /// <summary>
    /// Applies an edit to a logic node's configuration: name/description, entry name, gamebook-instructions flag,
    /// exit mode, accept-variables flag, and (SinglePath) the reconciled declared variables. Choices themselves are
    /// edited via <see cref="UpdateExitNode"/>. On a mode/flag change, container wires to outer ports this node no
    /// longer exposes are dropped.
    /// </summary>
    public void UpdateLogicNode(
        Guid                                                       containerId,
        Guid                                                       logicId,
        string                                                     name,
        string                                                     description,
        bool                                                       gamebookInstructions,
        StoryLogicExitMode                                         exitMode,
        bool                                                       acceptVariables,
        IReadOnlyList<(Guid Id, string Name, List<string> PossibleValues)> declaredVariables)
    {
        using var _ = Edit(); // node edit + container connection cleanup are one step
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;

        logic.Name                 = name;
        logic.Description          = description;
        logic.GamebookInstructions = gamebookInstructions;
        logic.ExitMode             = exitMode;
        logic.AcceptVariables      = acceptVariables;

        ReconcileDeclaredVariables(logic, declaredVariables);
        PruneContentConnections(logic);

        // Drop container wires that reference outer ports this node no longer exposes after a mode/flag change.
        List<Guid> goneOuterPorts = new();
        if (exitMode == StoryLogicExitMode.SinglePath)
            goneOuterPorts.AddRange(logic.Choices.Select(c => c.OuterFlowOut.Id)); // per-choice Flow outputs are gone
        else
            goneOuterPorts.Add(logic.VFlowOut.Id);                                 // the shared VFlow output is gone

        // The entry's type (Flow vs VFlow) follows acceptVariables — drop an incoming wire whose source type no longer matches.
        if (CurrentProject.ContainerNodes.TryGetValue(containerId, out StoryContainerNode? parent)
            && parent.Connections.Find(c => c.ToPoint == logic.EntryPoint.Id) is StoryConnection entryWire
            && CurrentProject.LogicNodes.Values.Any(l => l.VFlowOut.Id == entryWire.FromPoint) != acceptVariables)
            goneOuterPorts.Add(logic.EntryPoint.Id);

        RemoveConnectionsFor(containerId, goneOuterPorts);

        MarkKeyDirty(logicId);
        MarkKeyDirty(containerId);
    }

    /// <summary>Reconciles a logic node's declared variables (SinglePath), keeping matching ids and their per-choice values.</summary>
    private static void ReconcileDeclaredVariables(StoryLogicNode logic, IReadOnlyList<(Guid Id, string Name, List<string> PossibleValues)> desired)
    {
        List<StoryDeclaredVariable> rebuilt = new();
        foreach ((Guid id, string dname, List<string> values) in desired)
        {
            StoryDeclaredVariable dv = (id != Guid.Empty ? logic.DeclaredVariables.Find(d => d.Id == id) : null) ?? new StoryDeclaredVariable();
            dv.Name           = dname;
            dv.PossibleValues = values;
            rebuilt.Add(dv);
        }

        // Drop per-choice values for declared variables that no longer exist.
        HashSet<Guid> keepIds = new(rebuilt.Select(d => d.Id));
        foreach (StoryChoice choice in logic.Choices)
            choice.VariableValues.RemoveAll(v => !keepIds.Contains(v.DeclaredVarId));

        logic.DeclaredVariables.Clear();
        logic.DeclaredVariables.AddRange(rebuilt);
    }

    /// <summary>Removes inner content connections whose endpoints no longer exist.</summary>
    private static void PruneContentConnections(StoryLogicNode logic)
    {
        HashSet<Guid> valid = new()
        {
            logic.EntryPoint.Id, logic.TitleIn.Id, logic.SubtitleIn.Id, logic.IconIn.Id,
            logic.ExitLFlowIn.Id, logic.ExitVariablesIn.Id, logic.ExitAutoTextIn.Id
        };
        foreach (StoryChoice c in logic.Choices) valid.Add(c.TextIn.Id);
        foreach (StoryDeclaredVariable d in logic.DeclaredVariables) valid.Add(d.Id); // Prev Exit Variable outputs
        foreach (StoryLocalizationNode n in logic.LocalizationNodes) valid.Add(n.OutPoint.Id);
        foreach (StoryIconNode n in logic.IconNodes) valid.Add(n.OutPoint.Id);
        foreach (StoryLightDarkSwitchNode n in logic.LightDarkSwitchNodes)
        {
            valid.Add(n.DarkIn.Id);
            valid.Add(n.LightIn.Id);
            valid.Add(n.OutPoint.Id);
        }
        foreach (StorySmartFormatNode n in logic.SmartFormatNodes)
        {
            valid.Add(n.LocalizationIn.Id);
            valid.Add(n.VariablesIn.Id);
            valid.Add(n.OutPoint.Id);
        }
        foreach (StoryExternalVariableNode n in logic.ExternalVariableNodes) valid.Add(n.OutPoint.Id);
        foreach (StoryGetVariableNode n in logic.GetVariableNodes)
        {
            valid.Add(n.OutPoint.Id);
            valid.Add(n.SlotOutPoint.Id);
        }
        foreach (StoryConstantVariableNode n in logic.ConstantVariableNodes) valid.Add(n.OutPoint.Id);
        foreach (StoryFlowTextNode n in logic.FlowTextNodes)
        {
            valid.Add(n.FlowIn.Id);
            valid.Add(n.TextIn.Id);
            valid.Add(n.FlowOut.Id);
        }
        foreach (StorySplitForAppNode n in logic.SplitForAppNodes)
        {
            valid.Add(n.FlowIn.Id);
            valid.Add(n.FlowOut.Id);
        }
        foreach (StoryRegisterVariableNode n in logic.RegisterVariableNodes)
        {
            valid.Add(n.FlowIn.Id);
            valid.Add(n.FlowOut.Id);
            valid.Add(n.InstructionIn.Id);
            valid.Add(n.PlaceholderIn.Id);
        }
        foreach (StorySetVariableNode n in logic.SetVariableNodes)
        {
            valid.Add(n.FlowIn.Id);
            valid.Add(n.FlowOut.Id);
            valid.Add(n.InstructionIn.Id);
            valid.Add(n.PlaceholderIn.Id);
        }
        foreach (StoryUnregisterVariableNode n in logic.UnregisterVariableNodes)
        {
            valid.Add(n.FlowIn.Id);
            valid.Add(n.FlowOut.Id);
        }
        foreach (StorySetExternalVariableNode n in logic.SetExternalVariableNodes)
        {
            valid.Add(n.FlowIn.Id);
            valid.Add(n.FlowOut.Id);
            valid.Add(n.ValueIn.Id);
        }
        foreach (StoryLogicPortalNode n in logic.LogicPortalNodes)
        {
            valid.Add(n.InPoint.Id);
            foreach (StoryConnectionPoint o in n.OutPoints) valid.Add(o.Id);
        }
        foreach (StoryConditionFlowNode n in logic.ConditionFlowNodes)
        {
            valid.Add(n.FlowIn.Id);
            valid.Add(n.VariablesIn.Id);
            valid.Add(n.ContinueOut.Id);
            valid.Add(n.ConditionTrueOut.Id);
            valid.Add(n.EndFlowIn.Id);
        }

        logic.ContentConnections.RemoveAll(c => !valid.Contains(c.FromPoint) || !valid.Contains(c.ToPoint));
    }

    /// <summary>
    /// Applies an edit to a child container: new name/description and reconciled entry/exit point sets. Dropped
    /// points have their connections cleaned up both in the parent (<paramref name="parentContainerId"/>, where the
    /// container's ports are wired) and inside the container itself (where the boundary nodes are wired).
    /// </summary>
    public void UpdateContainerNode(
        Guid                                  parentContainerId,
        Guid                                  containerId,
        string                                name,
        string                                description,
        IReadOnlyList<(Guid Id, string Name)> entries,
        IReadOnlyList<(Guid Id, string Name)> exits)
    {
        using var _ = Edit(); // container edit + parent/self connection cleanup are one step
        if (!CurrentProject!.ContainerNodes.TryGetValue(containerId, out StoryContainerNode? container)) return;

        container.Name        = name;
        container.Description = description;

        ReconcilePoints(container.EntryPoints, entries, parentContainerId, containerId, isEntry: true);
        ReconcilePoints(container.ExitPoints,  exits,   parentContainerId, containerId, isEntry: false);

        MarkKeyDirty(containerId);
        MarkKeyDirty(parentContainerId);
    }

    /// <summary>
    /// Reconciles <paramref name="points"/> in place against the desired <paramref name="desired"/> rows: keeps and
    /// renames matching ids (in the desired order), appends new rows (empty id), and drops the rest — removing any
    /// connections that referenced dropped points from <paramref name="connCleanupContainerA"/> and the optional
    /// <paramref name="connCleanupContainerB"/>. New boundary points get a staggered canvas position when
    /// <paramref name="isEntry"/> is provided (they are drawn as nodes inside a container).
    /// </summary>
    private void ReconcilePoints(
        List<StoryConnectionPoint>            points,
        IReadOnlyList<(Guid Id, string Name)> desired,
        Guid                                  connCleanupContainerA,
        Guid?                                 connCleanupContainerB,
        bool?                                 isEntry = null)
    {
        List<StoryConnectionPoint> rebuilt = new();
        int                        newIndex = points.Count;

        foreach ((Guid id, string pname) in desired)
        {
            StoryConnectionPoint? existing = id != Guid.Empty ? points.Find(p => p.Id == id) : null;
            if (existing is not null)
            {
                existing.Name = pname;
                rebuilt.Add(existing);
            }
            else
            {
                StoryConnectionPoint created = new() { Name = pname };
                if (isEntry is not null)
                {
                    created.X = isEntry.Value ? 40 : 640;
                    created.Y = 120 + newIndex++ * 90;
                }
                rebuilt.Add(created);
            }
        }

        List<Guid> removed = points.Where(p => !rebuilt.Contains(p)).Select(p => p.Id).ToList();
        if (removed.Count > 0)
        {
            RemoveConnectionsFor(connCleanupContainerA, removed);
            if (connCleanupContainerB is Guid b) RemoveConnectionsFor(b, removed);
        }

        points.Clear();
        points.AddRange(rebuilt);
    }

    /// <summary>Removes every connection in <paramref name="containerId"/> that starts or ends at one of <paramref name="pointIds"/>.</summary>
    private void RemoveConnectionsFor(Guid containerId, IReadOnlyCollection<Guid> pointIds)
    {
        if (pointIds.Count == 0) return;
        if (!CurrentProject!.ContainerNodes.TryGetValue(containerId, out StoryContainerNode? container)) return;

        if (container.Connections.RemoveAll(c => pointIds.Contains(c.FromPoint) || pointIds.Contains(c.ToPoint)) > 0)
            MarkKeyDirty(containerId);
    }

    public void LoadProject(StoryProject project, LocProject? localization, string folderPath, Guid userId, Guid accessToken)
    {
        CurrentProject      = project;
        CurrentProjectPath  = folderPath;
        CurrentLocalization = localization;
        IsDirty             = false;
        ChangedFileIds.Clear();
        ResetHistory();
        ProjectChanged?.Invoke();
        DirtyStateChanged?.Invoke();
    }

    /// <summary>
    /// Opens the localization project referenced by <paramref name="project"/>'s metadata, via the shared
    /// library and the platform store factory. Returns null when the reference is empty or unresolvable
    /// (moved folder, deleted, or a handle minted on the other platform) — the caller re-links in that case.
    /// </summary>
    public async Task<LocProject?> ResolveLocalizationAsync(StoryProject project, string? storyLocation = null)
    {
        string stored = project.Metadata.LocalizationProjectPath;
        if (string.IsNullOrEmpty(stored)) return null;

        string locRef = ResolveLocReference(storyLocation ?? CurrentProjectPath, stored);

        try
        {
            return await DeusaldLocalizerCommon.ProjectFileService.OpenAsync(storeFactory.Create(locRef));
        }
        catch
        {
            return null;
        }
    }

    // ── Localization reference paths (stored relative to the story folder when possible) ──

    private static bool IsHandleReference(string reference) =>
        reference.StartsWith("loc:",   System.StringComparison.OrdinalIgnoreCase) ||
        reference.StartsWith("story:", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Converts a filesystem localization reference to a path relative to the story folder when possible (same
    /// root), so moving the whole project keeps the link. Web handles (<c>loc:</c>/<c>story:</c>), already-relative
    /// paths and cross-root paths are returned unchanged.
    /// </summary>
    private static string ToStoredLocReference(string? storyLocation, string reference)
    {
        if (string.IsNullOrEmpty(reference) || string.IsNullOrEmpty(storyLocation)) return reference;
        if (IsHandleReference(reference) || IsHandleReference(storyLocation!))       return reference;
        if (!Path.IsPathRooted(reference) || !Path.IsPathRooted(storyLocation!))     return reference;

        try
        {
            string rel = Path.GetRelativePath(storyLocation!, reference);
            return string.IsNullOrEmpty(rel) || Path.IsPathRooted(rel) ? reference : rel;
        }
        catch
        {
            return reference;
        }
    }

    /// <summary>Inverse of <see cref="ToStoredLocReference"/>: resolves a stored relative localization path back to
    /// an absolute one against the story folder. Absolute paths and web handles are returned unchanged.</summary>
    private static string ResolveLocReference(string? storyLocation, string reference)
    {
        if (string.IsNullOrEmpty(reference) || IsHandleReference(reference))            return reference;
        if (Path.IsPathRooted(reference) || string.IsNullOrEmpty(storyLocation))        return reference;
        if (IsHandleReference(storyLocation!))                                          return reference;

        try
        {
            return Path.GetFullPath(Path.Combine(storyLocation!, reference));
        }
        catch
        {
            return reference;
        }
    }

    /// <summary>
    /// Re-reads the linked localization project from its store so newly-added keys/categories show up in the
    /// pickers. Fires <see cref="ProjectDataChanged"/> so open UI (the key picker) refreshes. No-op with no project.
    /// </summary>
    public async Task RefreshLocalizationAsync()
    {
        if (CurrentProject is null) return;
        CurrentLocalization = await ResolveLocalizationAsync(CurrentProject);
        ProjectDataChanged?.Invoke();
    }

    /// <summary>
    /// Points <paramref name="project"/> at a new localization reference, persists the metadata, and returns
    /// the freshly opened loc project (null if the new reference still fails to resolve).
    /// </summary>
    public async Task<LocProject?> RelinkAndSaveAsync(StoryProject project, string storyLocation, string newReference)
    {
        project.Metadata.LocalizationProjectPath = ToStoredLocReference(storyLocation, newReference);
        await DeusaldStoryCommon.ProjectFileService.SaveMetadataOnlyAsync(project, storeFactory.Create(storyLocation));
        return await ResolveLocalizationAsync(project, storyLocation);
    }

    public void CloseProject()
    {
        CurrentProject      = null;
        CurrentProjectPath  = null;
        CurrentLocalization = null;
        IsDirty             = false;
        ChangedFileIds.Clear();
        ResetHistory();
        ProjectChanged?.Invoke();
        DirtyStateChanged?.Invoke();
    }

    public async Task SaveAsync()
    {
        if (string.IsNullOrEmpty(CurrentProjectPath))
        {
            string? saveLocation = await location.PickSaveLocationAsync(CurrentProject!.Metadata.Slug);

            if (!string.IsNullOrEmpty(saveLocation))
            {
                CurrentProjectPath = saveLocation;
                NormalizeLocReference();
                await DeusaldStoryCommon.ProjectFileService.SaveAsync(CurrentProject!, _CurrentStore);
                MarkClean();
            }
        }
        else
        {
            NormalizeLocReference();
            await DeusaldStoryCommon.ProjectFileService.SaveIncrementalAsync(CurrentProject!, _CurrentStore, ChangedFileIds);
            MarkClean();
        }

        if (!string.IsNullOrEmpty(CurrentProjectPath))
            recents.UpdateRecentProjects(CurrentProject!, CurrentProjectPath!, CurrentLocalization?.Keys.Count ?? 0);
    }

    /// <summary>Rewrites the metadata's localization reference relative to the story folder before a save, so a
    /// newly-saved or moved project keeps a portable link (no-op for web handles or cross-root paths).</summary>
    private void NormalizeLocReference()
    {
        if (CurrentProject is null || string.IsNullOrEmpty(CurrentProjectPath)) return;
        CurrentProject.Metadata.LocalizationProjectPath =
            ToStoredLocReference(CurrentProjectPath, CurrentProject.Metadata.LocalizationProjectPath);
    }

    /// <summary>Marks a key as edited.</summary>
    public void MarkKeyDirty(Guid keyId)
    {
        ChangedFileIds.Add(keyId);
        TrackTouch(keyId);
        RaiseDirty();
    }

    /// <summary>Marks the project dirty without a specific key — used for metadata-only edits (e.g. End unregister).</summary>
    public void MarkDirty()
    {
        TrackTouch(_MetadataKey);
        RaiseDirty();
    }

    private void RaiseDirty()
    {
        if (!IsDirty)
        {
            IsDirty = true;
            DirtyStateChanged?.Invoke();
        }
        // Always notify data listeners, even if IsDirty was already true —
        // otherwise edits after the first one in a session go unnoticed by
        // components that only refresh on this event (e.g. progress bars).
        ProjectDataChanged?.Invoke();
    }

    public void MarkClean()
    {
        if (!IsDirty) return;
        IsDirty = false;
        DirtyStateChanged?.Invoke();
    }
}