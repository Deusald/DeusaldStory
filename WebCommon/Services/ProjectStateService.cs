using DeusaldLocalizerCommon;
using DeusaldStoryCommon;
using JetBrains.Annotations;

namespace DeusaldStoryWeb;

/// <summary>
/// Holds the currently open project and active user for the lifetime of the app session.
/// Inject as a singleton so all pages share the same state.
/// </summary>
[PublicAPI]
public class ProjectStateService(
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
            new StoryConnectionPoint { Name = "In" },
            new[] { new StoryConnectionPoint { Name = "Out" } }, 340, 120);
        StoryContainerNode container = AddContainerNode(root, "Test Container", "A nested container",
            new[] { new StoryConnectionPoint { Name = "In" } },
            new[] { new StoryConnectionPoint { Name = "Out" } }, 340, 340);

        // Pre-wire Start → logic → container → End so edges are visible immediately.
        StoryContainerNode rootNode = project.ContainerNodes[root];
        Connect(root, rootNode.EntryPoints[0].Id, logic.EntryPoint.Id);
        Connect(root, logic.ExitPoints[0].Id, container.EntryPoints[0].Id);
        Connect(root, container.ExitPoints[0].Id, rootNode.ExitPoints[0].Id);
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
        IEnumerable<StoryConnectionPoint> exitPoints,
        double                            x,
        double                            y)
    {
        StoryLogicNode node = new()
        {
            Name            = name,
            Description     = description,
            ParentContainer = parentContainerId,
            EntryPoint      = entryPoint,
            X               = x,
            Y               = y
        };
        node.ExitPoints.AddRange(exitPoints);

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

    /// <summary>Places a logic node's Entry point and each Exit point on its inner canvas so they don't overlap.</summary>
    private static void LayoutLogicInnerPoints(StoryLogicNode logic)
    {
        logic.EntryPoint.X = 60;
        logic.EntryPoint.Y = 200;
        for (int x = 0; x < logic.ExitPoints.Count; ++x)
        {
            logic.ExitPoints[x].X = 660;
            logic.ExitPoints[x].Y = 120 + x * 90;
        }
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

    /// <summary>
    /// Wires an output (<paramref name="fromPoint"/>) to an input (<paramref name="toPoint"/>) inside a logic node's
    /// content graph. An output leads to one place and a Title/Icon input takes a single source, so any existing wire
    /// on either endpoint is replaced. A no-op (returns null) if the exact wire already exists.
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

        StoryConnectionPoint? exit = logic.ExitPoints.Find(p => p.Id == movedId);
        if (exit is not null)
        {
            exit.X = x;
            exit.Y = y;
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
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;

        List<Guid> pointIds = [logic.EntryPoint.Id, .. logic.ExitPoints.Select(p => p.Id)];

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
    /// Applies an edit to a logic node: new name/description and a reconciled set of exit points. Existing exits
    /// (matched by id) are renamed and reordered, brand-new rows (empty id) are added, and dropped exits have their
    /// connections removed. The single entry point keeps its id and is only renamed.
    /// </summary>
    public void UpdateLogicNode(
        Guid                                  containerId,
        Guid                                  logicId,
        string                                name,
        string                                description,
        string                                entryName,
        IReadOnlyList<(Guid Id, string Name)> exits)
    {
        if (!CurrentProject!.LogicNodes.TryGetValue(logicId, out StoryLogicNode? logic)) return;

        logic.Name             = name;
        logic.Description      = description;
        logic.EntryPoint.Name  = entryName;

        // isEntry:false gives each newly-added exit an inner-graph position (it is drawn as an Exit node inside).
        ReconcilePoints(logic.ExitPoints, exits, containerId, null, isEntry: false);

        // A removed exit takes its inner Exit node with it — drop any inner wire that pointed at the gone port.
        PruneContentConnections(logic);

        MarkKeyDirty(logicId);
        MarkKeyDirty(containerId);
    }

    /// <summary>Removes inner content connections whose endpoints no longer exist (e.g. after an exit is deleted).</summary>
    private static void PruneContentConnections(StoryLogicNode logic)
    {
        HashSet<Guid> valid = new() { logic.EntryPoint.Id, logic.TitleIn.Id, logic.IconIn.Id };
        foreach (StoryConnectionPoint p in logic.ExitPoints) valid.Add(p.Id);
        foreach (StoryLocalizationNode n in logic.LocalizationNodes) valid.Add(n.OutPoint.Id);
        foreach (StoryIconNode n in logic.IconNodes) valid.Add(n.OutPoint.Id);
        foreach (StoryLightDarkSwitchNode n in logic.LightDarkSwitchNodes)
        {
            valid.Add(n.DarkIn.Id);
            valid.Add(n.LightIn.Id);
            valid.Add(n.OutPoint.Id);
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
        ProjectChanged?.Invoke();
        DirtyStateChanged?.Invoke();
    }

    /// <summary>
    /// Opens the localization project referenced by <paramref name="project"/>'s metadata, via the shared
    /// library and the platform store factory. Returns null when the reference is empty or unresolvable
    /// (moved folder, deleted, or a handle minted on the other platform) — the caller re-links in that case.
    /// </summary>
    public async Task<LocProject?> ResolveLocalizationAsync(StoryProject project)
    {
        string locRef = project.Metadata.LocalizationProjectPath;
        if (string.IsNullOrEmpty(locRef)) return null;

        try
        {
            return await DeusaldLocalizerCommon.ProjectFileService.OpenAsync(storeFactory.Create(locRef));
        }
        catch
        {
            return null;
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
        project.Metadata.LocalizationProjectPath = newReference;
        await DeusaldStoryCommon.ProjectFileService.SaveMetadataOnlyAsync(project, storeFactory.Create(storyLocation));
        return await ResolveLocalizationAsync(project);
    }

    public void CloseProject()
    {
        CurrentProject      = null;
        CurrentProjectPath  = null;
        CurrentLocalization = null;
        IsDirty             = false;
        ChangedFileIds.Clear();
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
                await DeusaldStoryCommon.ProjectFileService.SaveAsync(CurrentProject!, _CurrentStore);
                MarkClean();
            }
        }
        else
        {
            await DeusaldStoryCommon.ProjectFileService.SaveIncrementalAsync(CurrentProject!, _CurrentStore, ChangedFileIds);
            MarkClean();
        }

        if (!string.IsNullOrEmpty(CurrentProjectPath))
            recents.UpdateRecentProjects(CurrentProject!, CurrentProjectPath!, CurrentLocalization?.Keys.Count ?? 0);
    }

    /// <summary>Marks a key as edited.</summary>
    public void MarkKeyDirty(Guid keyId)
    {
        ChangedFileIds.Add(keyId);
        MarkDirty();
    }

    public void MarkDirty()
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