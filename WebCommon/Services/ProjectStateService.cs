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
        Guid                 parentContainerId,
        string               name,
        string               description,
        StoryConnectionPoint entryPoint,
        double               x,
        double               y,
        bool                 gamebookInstructions = false,
        StoryLogicExitMode   exitMode             = StoryLogicExitMode.ManyPaths)
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
            ExitMode             = exitMode
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
        double                            y,
        IEnumerable<Guid>?                usedVariables = null)
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
        if (usedVariables is not null) node.UsedVariables.AddRange(usedVariables);

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
                ? [logic.EntryPoint.Id, logic.SingleOut.Id]
                : [logic.EntryPoint.Id, .. logic.Choices.Select(c => c.OuterFlowOut.Id)];
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
        return AddPortalIn(pointId, x, y);
    }

    /// <summary>Adds another <b>portal in</b> to the pair that owns <paramref name="pointId"/> at the explicit canvas position (<paramref name="x"/>, <paramref name="y"/>). Returns the portal, or null when the point belongs to no portal.</summary>
    public StoryPortalNode? AddPortalIn(Guid pointId, double x, double y)
    {
        StoryPortalNode? portal = FindPortalByPoint(pointId);
        if (portal is null) return null;

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

    // ── Logic node inner content graph ────────────────────────────────────────
    // Every inner node has exactly one flow in and one flow out. What a node *does* is configured in the inspector
    // (which mutates the POCO directly and calls MarkKeyDirty), so the service only owns creation, deletion and
    // wiring — the operations that touch more than the node itself.

    /// <summary>Places a logic node's Entry and Exit nodes on its inner canvas so they don't overlap.</summary>
    private static void LayoutLogicInnerPoints(StoryLogicNode logic)
    {
        logic.EntryPoint.X = 60;
        logic.EntryPoint.Y = 200;
        logic.ExitFlowIn.X = 660;
        logic.ExitFlowIn.Y = 200;
    }

    /// <summary>Adds a Text node to a logic node's inner graph.</summary>
    public StoryTextNode? AddTextNode(Guid logicId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StoryTextNode node = new() { X = x, Y = y };
        logic.TextNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>Removes a Text node and every wire touching it.</summary>
    public void DeleteTextNode(Guid logicId, Guid nodeId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        if (logic.TextNodes.Find(n => n.Id == nodeId) is not StoryTextNode node) return;

        logic.TextNodes.Remove(node);
        RemoveContentWires(logic, node.FlowIn.Id, node.FlowOut.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>Adds a Split-for-App node to a logic node's inner graph.</summary>
    public StorySplitForAppNode? AddSplitForAppNode(Guid logicId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StorySplitForAppNode node = new() { X = x, Y = y };
        logic.SplitForAppNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>Removes a Split-for-App node and every wire touching it.</summary>
    public void DeleteSplitForAppNode(Guid logicId, Guid nodeId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        if (logic.SplitForAppNodes.Find(n => n.Id == nodeId) is not StorySplitForAppNode node) return;

        logic.SplitForAppNodes.Remove(node);
        RemoveContentWires(logic, node.FlowIn.Id, node.FlowOut.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>Adds a Constant Variable node to a logic node's inner graph.</summary>
    public StoryConstantVariableNode? AddConstantVariableNode(Guid logicId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StoryConstantVariableNode node = new() { X = x, Y = y };
        logic.ConstantVariableNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>Removes a Constant Variable node and every wire touching it.</summary>
    public void DeleteConstantVariableNode(Guid logicId, Guid nodeId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        if (logic.ConstantVariableNodes.Find(n => n.Id == nodeId) is not StoryConstantVariableNode node) return;

        logic.ConstantVariableNodes.Remove(node);
        RemoveContentWires(logic, node.FlowIn.Id, node.FlowOut.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>Adds a Randomized Instruction node to a logic node's inner graph.</summary>
    public StoryRandomizedInstructionNode? AddRandomizedInstructionNode(Guid logicId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StoryRandomizedInstructionNode node = new() { X = x, Y = y };
        logic.RandomizedInstructionNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>Removes a Randomized Instruction node and every wire touching it.</summary>
    public void DeleteRandomizedInstructionNode(Guid logicId, Guid nodeId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        if (logic.RandomizedInstructionNodes.Find(n => n.Id == nodeId) is not StoryRandomizedInstructionNode node) return;

        logic.RandomizedInstructionNodes.Remove(node);
        RemoveContentWires(logic, node.FlowIn.Id, node.FlowOut.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>Adds a Set Variable node to a logic node's inner graph.</summary>
    public StorySetVariableNode? AddSetVariableNode(Guid logicId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StorySetVariableNode node = new() { X = x, Y = y };
        logic.SetVariableNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>Removes a Set Variable node and every wire touching it.</summary>
    public void DeleteSetVariableNode(Guid logicId, Guid nodeId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        if (logic.SetVariableNodes.Find(n => n.Id == nodeId) is not StorySetVariableNode node) return;

        logic.SetVariableNodes.Remove(node);
        RemoveContentWires(logic, node.FlowIn.Id, node.FlowOut.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>Drops every inner wire touching any of <paramref name="points"/> (a deleted node's ports).</summary>
    private static void RemoveContentWires(StoryLogicNode logic, params Guid[] points)
    {
        HashSet<Guid> gone = new(points);
        logic.ContentConnections.RemoveAll(c => gone.Contains(c.FromPoint) || gone.Contains(c.ToPoint));
    }

    // ── Condition-flow pairs (inner-graph optional flow blocks) ──────────────

    /// <summary>
    /// Adds a Condition pair — the gate card at (<paramref name="x"/>, <paramref name="y"/>) and its paired end card
    /// to its right. The two are one object, so deleting either removes both.
    /// </summary>
    public StoryConditionFlowNode? AddConditionFlowNode(Guid logicId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StoryConditionFlowNode node = new()
        {
            X    = x,
            Y    = y,
            EndX = x + _PORTAL_PAIR_GAP,
            EndY = y
        };
        logic.ConditionFlowNodes.Add(node);
        MarkKeyDirty(logicId);
        return node;
    }

    /// <summary>Removes a Condition pair (given either half's id) and every wire touching either card.</summary>
    public void DeleteConditionFlowNode(Guid logicId, Guid anyId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        if (FindConditionFlowByAnyId(logic, anyId) is not StoryConditionFlowNode node) return;

        logic.ConditionFlowNodes.Remove(node);
        RemoveContentWires(logic, node.FlowIn.Id, node.ContinueOut.Id, node.ConditionTrueOut.Id, node.EndFlowIn.Id);
        MarkKeyDirty(logicId);
    }

    /// <summary>The Condition pair owning <paramref name="anyId"/> — either the gate card or its paired end card.</summary>
    public StoryConditionFlowNode? FindConditionFlowByAnyId(StoryLogicNode logic, Guid anyId) =>
        logic.ConditionFlowNodes.Find(n => n.Id == anyId || n.EndId == anyId);

    // ── Choices (ManyPaths / HubPaths) ───────────────────────────────────────

    /// <summary>Adds a continuation to a logic node. Its new outer port needs wiring in the parent container.</summary>
    public StoryChoice? AddChoice(Guid logicId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;

        StoryChoice choice = new();
        logic.Choices.Add(choice);
        MarkKeyDirty(logicId);
        return choice;
    }

    /// <summary>Removes a continuation, pruning the outer wire that left its port in the parent container.</summary>
    public void RemoveChoice(Guid logicId, Guid choiceId)
    {
        using var _ = Edit(); // the choice and the parent container's wire change together
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        if (logic.Choices.Find(c => c.Id == choiceId) is not StoryChoice choice) return;

        logic.Choices.Remove(choice);
        RemoveConnectionsFor(logic.ParentContainer, [choice.OuterFlowOut.Id]);
        MarkKeyDirty(logicId);
        MarkKeyDirty(logic.ParentContainer);
    }

    /// <summary>
    /// Switches a logic node's exit mode. The modes draw different outer ports — SinglePath one shared output,
    /// the others one per choice — so every wire leaving a port the new mode doesn't draw is pruned.
    /// </summary>
    public void SetLogicExitMode(Guid logicId, StoryLogicExitMode mode)
    {
        using var _ = Edit(); // the mode and the parent container's outer wires change together
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        if (logic.ExitMode == mode) return;

        List<Guid> gone = mode == StoryLogicExitMode.SinglePath
            ? logic.Choices.Select(c => c.OuterFlowOut.Id).ToList()   // the per-choice ports disappear
            : [logic.SingleOut.Id];                                    // the shared port disappears

        logic.ExitMode = mode;
        RemoveConnectionsFor(logic.ParentContainer, gone);
        MarkKeyDirty(logicId);
        MarkKeyDirty(logic.ParentContainer);
    }

    // ── Choice definitions (SinglePath) ──────────────────────────────────────

    /// <summary>
    /// Adds a choice definition, up to <see cref="StoryChoiceVariables.MAX_DEFINITIONS"/> (one per Choice variable).
    /// Returns null when the node already has that many.
    /// </summary>
    public StoryChoiceDefinition? AddChoiceDefinition(Guid logicId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;
        if (logic.ChoiceDefinitions.Count >= StoryChoiceVariables.MAX_DEFINITIONS) return null;

        StoryChoiceDefinition def = new();
        logic.ChoiceDefinitions.Add(def);
        MarkKeyDirty(logicId);
        return def;
    }

    /// <summary>
    /// Removes a choice definition. The remaining ones shift up, so which Choice variable each writes changes with
    /// their new positions — that is inherent to positional mapping, not something the service can preserve.
    /// </summary>
    public void RemoveChoiceDefinition(Guid logicId, Guid definitionId)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;
        if (logic.ChoiceDefinitions.RemoveAll(d => d.Id == definitionId) > 0) MarkKeyDirty(logicId);
    }

    // ── Comment notes ──────────────────────────────────────────────────────

    /// <summary>Adds a free-text comment note to a container's graph or a logic node's inner graph.</summary>
    public StoryCommentNode? AddCommentNode(Guid ownerId, string text, double x, double y)
    {
        StoryCommentNode node = new() { Text = text, X = x, Y = y };

        if (CurrentProject!.LogicNodes.TryGetValue(ownerId, out StoryLogicNode? logic)) logic.CommentNodes.Add(node);
        else if (CurrentProject.ContainerNodes.TryGetValue(ownerId, out StoryContainerNode? container)) container.Comments.Add(node);
        else return null;

        MarkKeyDirty(ownerId);
        return node;
    }

    /// <summary>Persists a comment note's dragged position.</summary>
    public void MoveCommentNode(Guid ownerId, Guid nodeId, double x, double y)
    {
        if (FindCommentNode(ownerId, nodeId) is not StoryCommentNode node) return;
        node.X = x;
        node.Y = y;
        MarkKeyDirty(ownerId);
    }

    /// <summary>Removes a comment note (it has no ports, so nothing else references it).</summary>
    public void DeleteCommentNode(Guid ownerId, Guid nodeId)
    {
        if (CurrentProject!.LogicNodes.TryGetValue(ownerId, out StoryLogicNode? logic))
        {
            if (logic.CommentNodes.RemoveAll(n => n.Id == nodeId) > 0) MarkKeyDirty(ownerId);
            return;
        }
        if (CurrentProject.ContainerNodes.TryGetValue(ownerId, out StoryContainerNode? container))
            if (container.Comments.RemoveAll(n => n.Id == nodeId) > 0) MarkKeyDirty(ownerId);
    }

    /// <summary>The comment note with <paramref name="nodeId"/> in either kind of owner graph.</summary>
    public StoryCommentNode? FindCommentNode(Guid ownerId, Guid nodeId)
    {
        if (CurrentProject!.LogicNodes.TryGetValue(ownerId, out StoryLogicNode? logic))
            return logic.CommentNodes.Find(n => n.Id == nodeId);
        if (CurrentProject.ContainerNodes.TryGetValue(ownerId, out StoryContainerNode? container))
            return container.Comments.Find(n => n.Id == nodeId);
        return null;
    }

    /// <summary>
    /// Wires an output to an input inside a logic node's content graph. Every port now carries plain flow and leads
    /// to exactly one place, so both the source's previous wire and the target's previous wire are replaced.
    /// A no-op (returns null) if the exact wire already exists.
    /// </summary>
    public StoryConnection? ConnectContent(Guid logicId, Guid fromPoint, Guid toPoint)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return null;
        if (logic.ContentConnections.Exists(c => c.FromPoint == fromPoint && c.ToPoint == toPoint)) return null;

        logic.ContentConnections.RemoveAll(c => c.FromPoint == fromPoint || c.ToPoint == toPoint);

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
    /// Persists a drag inside a logic node's inner graph. <paramref name="movedId"/> is the dragged EdNode id — the
    /// Entry point, the Exit point, either half of a Condition pair, or any content node — resolved to the right
    /// stored position.
    /// </summary>
    public void MoveLogicNode(Guid logicId, Guid movedId, double x, double y)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;

        bool Place(Guid id, Action<double, double> set)
        {
            if (id != movedId) return false;
            set(x, y);
            MarkKeyDirty(logicId);
            return true;
        }

        if (Place(logic.EntryPoint.Id, (px, py) => { logic.EntryPoint.X = px; logic.EntryPoint.Y = py; })) return;
        if (Place(logic.ExitFlowIn.Id, (px, py) => { logic.ExitFlowIn.X = px; logic.ExitFlowIn.Y = py; })) return;

        foreach (StoryTextNode n in logic.TextNodes)
            if (Place(n.Id, (px, py) => { n.X = px; n.Y = py; })) return;

        foreach (StorySplitForAppNode n in logic.SplitForAppNodes)
            if (Place(n.Id, (px, py) => { n.X = px; n.Y = py; })) return;

        foreach (StoryConstantVariableNode n in logic.ConstantVariableNodes)
            if (Place(n.Id, (px, py) => { n.X = px; n.Y = py; })) return;

        foreach (StorySetVariableNode n in logic.SetVariableNodes)
            if (Place(n.Id, (px, py) => { n.X = px; n.Y = py; })) return;

        foreach (StoryRandomizedInstructionNode n in logic.RandomizedInstructionNodes)
            if (Place(n.Id, (px, py) => { n.X = px; n.Y = py; })) return;

        // A Condition pair draws two cards from one object — each has its own stored position.
        foreach (StoryConditionFlowNode n in logic.ConditionFlowNodes)
        {
            if (Place(n.Id,    (px, py) => { n.X    = px; n.Y    = py; })) return;
            if (Place(n.EndId, (px, py) => { n.EndX = px; n.EndY = py; })) return;
        }

        foreach (StoryCommentNode n in logic.CommentNodes)
            if (Place(n.Id, (px, py) => { n.X = px; n.Y = py; })) return;
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

    /// <summary>Marks a variable dirty after the panel has edited its fields in place. No-op when the id is unknown.</summary>
    public void MarkVariableDirty(Guid id)
    {
        if (!CurrentProject!.Variables.ContainsKey(id)) return;
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

        List<Guid> pointIds = [logic.EntryPoint.Id, logic.SingleOut.Id, .. logic.Choices.Select(c => c.OuterFlowOut.Id)];

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
    /// Removes inner content connections whose endpoints no longer exist. Every port is a flow port now, so the
    /// valid set is simply the Entry, the Exit, and each content node's own two (or, for a Condition pair, four).
    /// </summary>
    private static void PruneContentConnections(StoryLogicNode logic)
    {
        HashSet<Guid> valid = new() { logic.EntryPoint.Id, logic.ExitFlowIn.Id };

        foreach (StoryTextNode n in logic.TextNodes)
        {
            valid.Add(n.FlowIn.Id);
            valid.Add(n.FlowOut.Id);
        }
        foreach (StorySplitForAppNode n in logic.SplitForAppNodes)
        {
            valid.Add(n.FlowIn.Id);
            valid.Add(n.FlowOut.Id);
        }
        foreach (StoryConstantVariableNode n in logic.ConstantVariableNodes)
        {
            valid.Add(n.FlowIn.Id);
            valid.Add(n.FlowOut.Id);
        }
        foreach (StorySetVariableNode n in logic.SetVariableNodes)
        {
            valid.Add(n.FlowIn.Id);
            valid.Add(n.FlowOut.Id);
        }
        foreach (StoryRandomizedInstructionNode n in logic.RandomizedInstructionNodes)
        {
            valid.Add(n.FlowIn.Id);
            valid.Add(n.FlowOut.Id);
        }
        foreach (StoryConditionFlowNode n in logic.ConditionFlowNodes)
        {
            valid.Add(n.FlowIn.Id);
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
        Guid                                                     parentContainerId,
        Guid                                                     containerId,
        string                                                   name,
        string                                                   description,
        IReadOnlyList<(Guid Id, string Name)>                     entries,
        IReadOnlyList<(Guid Id, string Name)>                     exits,
        IEnumerable<Guid>?                                       usedVariables = null)
    {
        using var _ = Edit(); // container edit + parent/self connection cleanup are one step
        if (!CurrentProject!.ContainerNodes.TryGetValue(containerId, out StoryContainerNode? container)) return;

        container.Name        = name;
        container.Description = description;

        if (usedVariables is not null)
        {
            container.UsedVariables.Clear();
            container.UsedVariables.AddRange(usedVariables);
        }

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
        List<StoryConnectionPoint>                                 points,
        IReadOnlyList<(Guid Id, string Name)>                       desired,
        Guid                                                       connCleanupContainerA,
        Guid?                                                      connCleanupContainerB,
        bool?                                                      isEntry = null)
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

    /// <summary>
    /// Re-links the <b>currently open</b> project to <paramref name="newReference"/>, persists the metadata, swaps in
    /// the new <see cref="CurrentLocalization"/> and notifies open UI. Returns false when there is no open project or
    /// the new reference can't be opened. Used by the editor's Localization panel to change the link in place.
    /// </summary>
    public async Task<bool> RelinkCurrentLocalizationAsync(string newReference)
    {
        if (CurrentProject is null || string.IsNullOrEmpty(CurrentProjectPath)) return false;

        LocProject? loc = await RelinkAndSaveAsync(CurrentProject, CurrentProjectPath!, newReference);
        if (loc is null) return false;

        CurrentLocalization = loc;
        ProjectDataChanged?.Invoke();
        return true;
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
        FlushCoalesced(); // a pending live edit becomes its own undo step before it is written to disc

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