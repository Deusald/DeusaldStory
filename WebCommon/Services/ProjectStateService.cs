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
            string? saveLocation = await location.PickSaveLocationAsync();

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