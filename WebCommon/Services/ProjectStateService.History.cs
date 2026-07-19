using DeusaldStoryCommon;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DeusaldStoryWeb;

/// <summary>
/// In-memory Undo/Redo for the open project. History lives only for the session — it is never persisted and is
/// wiped whenever the project changes (<see cref="ResetHistory"/> on load / new / close).
///
/// The model mutates through this service's methods, which all funnel through
/// <see cref="MarkKeyDirty"/> / <see cref="MarkDirty"/>. We piggyback on that choke point: each atomic action records
/// the before/after serialized JSON of exactly the entities it changed. A "shadow" baseline holds the last-committed
/// JSON of every entity, so a commit only re-serialises the touched entities (node drags are frequent — a full-project
/// snapshot per action would be far too heavy).
///
/// Atomicity is a reentrant transaction (<see cref="Edit"/> / <see cref="BeginBatch"/>): the outermost scope commits
/// one <see cref="HistoryEntry"/> covering every entity touched inside it. A bare mutation with no scope active
/// auto-commits as its own single-entity step.
/// </summary>
public partial class ProjectStateService
{
    // ── Types ──────────────────────────────────────────────────────────────

    private enum EntityKind { Metadata, Container, Logic, Portal, Image, Variable }

    /// <summary>One entity's change within an action: null Json means the entity did not exist / was deleted.</summary>
    private readonly record struct HistoryChange(EntityKind Kind, Guid Id, string? Before, string? After);

    private sealed class HistoryEntry
    {
        public List<HistoryChange> Changes { get; } = new();
    }

    // Metadata has no id in the entity dictionaries, so it is tracked under this fixed sentinel key.
    private static readonly Guid _MetadataKey = new("00000000-0000-0000-0000-0000000000ff");

    private const int _MAX_HISTORY = 100;

    private static readonly JsonSerializerSettings _HistoryJson = new()
    {
        Formatting        = Formatting.None,
        NullValueHandling = NullValueHandling.Include,
        Converters        = { new StringEnumConverter() },
    };

    // ── State ──────────────────────────────────────────────────────────────

    private readonly Dictionary<Guid, (EntityKind Kind, string Json)> _Shadow = new();
    private readonly List<HistoryEntry> _Undo = new(); // end = newest
    private readonly List<HistoryEntry> _Redo = new(); // end = newest

    private          int           _TxnDepth;
    private readonly HashSet<Guid> _TxnTouched = new();
    private          bool          _Replaying;

    public bool CanUndo => _Undo.Count > 0;
    public bool CanRedo => _Redo.Count > 0;

    /// <summary>Fires when the undo/redo stacks change (push, undo, redo, reset) — refresh button enabled state.</summary>
    public event Action? HistoryChanged;

    /// <summary>Fires after an Undo/Redo reverts the model — open views must rebuild from the changed data.</summary>
    public event Action? HistoryApplied;

    // ── Transaction scope ──────────────────────────────────────────────────

    /// <summary>Opens a reentrant edit scope. The outermost scope's dispose commits one atomic history entry.</summary>
    private IDisposable Edit() => new EditScope(this);

    /// <summary>
    /// Groups several service calls into a single atomic undo step (e.g. a multi-selection drag). Same primitive as
    /// the per-method <see cref="Edit"/> scope; exposed for callers that orchestrate more than one mutation.
    /// </summary>
    public IDisposable BeginBatch() => new EditScope(this);

    private sealed class EditScope : IDisposable
    {
        private readonly ProjectStateService _State;
        private          bool                _Disposed;

        public EditScope(ProjectStateService state)
        {
            _State = state;
            _State._TxnDepth++;
        }

        public void Dispose()
        {
            if (_Disposed) return;
            _Disposed = true;
            if (--_State._TxnDepth == 0)
                _State.CommitTransaction();
        }
    }

    // ── Touch tracking (called from MarkKeyDirty / MarkDirty) ───────────────

    /// <summary>Records that <paramref name="id"/> changed in the active action, committing immediately if unscoped.</summary>
    private void TrackTouch(Guid id)
    {
        if (_Replaying) return;
        _TxnTouched.Add(id);
        if (_TxnDepth == 0) CommitTransaction();
    }

    private void CommitTransaction()
    {
        if (_Replaying || _TxnTouched.Count == 0)
        {
            _TxnTouched.Clear();
            return;
        }

        HistoryEntry entry = new();
        foreach (Guid id in _TxnTouched)
        {
            string?    after       = SerializeEntity(id, out EntityKind afterKind, out bool afterExists);
            bool       beforeExists = _Shadow.TryGetValue(id, out (EntityKind Kind, string Json) before);
            string?    beforeJson   = beforeExists ? before.Json : null;

            if (!afterExists && !beforeExists)            continue; // nothing to record
            if (afterExists && beforeExists && after == beforeJson) continue; // no net change

            EntityKind kind = afterExists ? afterKind : before.Kind;
            entry.Changes.Add(new HistoryChange(kind, id, beforeJson, after));

            if (afterExists) _Shadow[id] = (kind, after!);
            else             _Shadow.Remove(id);
        }
        _TxnTouched.Clear();

        if (entry.Changes.Count == 0) return;

        _Undo.Add(entry);
        if (_Undo.Count > _MAX_HISTORY) _Undo.RemoveAt(0);
        _Redo.Clear();
        HistoryChanged?.Invoke();
    }

    // ── Undo / Redo ────────────────────────────────────────────────────────

    public void Undo()
    {
        if (_Undo.Count == 0) return;
        HistoryEntry entry = _Undo[^1];
        _Undo.RemoveAt(_Undo.Count - 1);
        ApplyEntry(entry, redo: false);
        _Redo.Add(entry);
        HistoryChanged?.Invoke();
        HistoryApplied?.Invoke();
    }

    public void Redo()
    {
        if (_Redo.Count == 0) return;
        HistoryEntry entry = _Redo[^1];
        _Redo.RemoveAt(_Redo.Count - 1);
        ApplyEntry(entry, redo: true);
        _Undo.Add(entry);
        HistoryChanged?.Invoke();
        HistoryApplied?.Invoke();
    }

    /// <summary>Applies each change in the entry (Before for undo, After for redo) and keeps the shadow in step.</summary>
    private void ApplyEntry(HistoryEntry entry, bool redo)
    {
        _Replaying = true;
        try
        {
            foreach (HistoryChange ch in entry.Changes)
            {
                string? target = redo ? ch.After : ch.Before;
                SetEntity(ch.Kind, ch.Id, target);

                if (target is null) _Shadow.Remove(ch.Id);
                else                _Shadow[ch.Id] = (ch.Kind, target);

                if (ch.Id != _MetadataKey) ChangedFileIds.Add(ch.Id);
            }
        }
        finally
        {
            _Replaying = false;
        }
        RaiseDirty();
    }

    // ── Baseline / reset ───────────────────────────────────────────────────

    /// <summary>Clears all history and rebuilds the shadow baseline from the current project. Call on load / new / close.</summary>
    public void ResetHistory()
    {
        _Undo.Clear();
        _Redo.Clear();
        _TxnTouched.Clear();
        _TxnDepth  = 0;
        _Replaying = false;
        _Shadow.Clear();

        if (CurrentProject is StoryProject p)
        {
            _Shadow[_MetadataKey] = (EntityKind.Metadata, Serialize(p.Metadata));
            foreach (KeyValuePair<Guid, StoryContainerNode> kv in p.ContainerNodes) _Shadow[kv.Key] = (EntityKind.Container, Serialize(kv.Value));
            foreach (KeyValuePair<Guid, StoryLogicNode>     kv in p.LogicNodes)     _Shadow[kv.Key] = (EntityKind.Logic,     Serialize(kv.Value));
            foreach (KeyValuePair<Guid, StoryPortalNode>    kv in p.PortalNodes)    _Shadow[kv.Key] = (EntityKind.Portal,    Serialize(kv.Value));
            foreach (KeyValuePair<Guid, StoryImage>         kv in p.Images)         _Shadow[kv.Key] = (EntityKind.Image,     Serialize(kv.Value));
            foreach (KeyValuePair<Guid, StoryVariable>      kv in p.Variables)      _Shadow[kv.Key] = (EntityKind.Variable,  Serialize(kv.Value));
        }
        HistoryChanged?.Invoke();
    }

    // ── Entity read / write ────────────────────────────────────────────────

    /// <summary>Serialises the entity with <paramref name="id"/>, resolving its kind. <paramref name="exists"/> is false when it is gone.</summary>
    private string? SerializeEntity(Guid id, out EntityKind kind, out bool exists)
    {
        exists = false;
        kind   = EntityKind.Metadata;
        if (CurrentProject is not StoryProject p) return null;

        if (id == _MetadataKey)                                             { kind = EntityKind.Metadata;  exists = true; return Serialize(p.Metadata); }
        if (p.ContainerNodes.TryGetValue(id, out StoryContainerNode? c))    { kind = EntityKind.Container; exists = true; return Serialize(c); }
        if (p.LogicNodes.TryGetValue(id, out StoryLogicNode? l))            { kind = EntityKind.Logic;     exists = true; return Serialize(l); }
        if (p.PortalNodes.TryGetValue(id, out StoryPortalNode? po))         { kind = EntityKind.Portal;    exists = true; return Serialize(po); }
        if (p.Images.TryGetValue(id, out StoryImage? im))                   { kind = EntityKind.Image;     exists = true; return Serialize(im); }
        if (p.Variables.TryGetValue(id, out StoryVariable? v))              { kind = EntityKind.Variable;  exists = true; return Serialize(v); }
        return null;
    }

    /// <summary>Writes an entity back from its stored JSON, or removes it when <paramref name="json"/> is null.</summary>
    private void SetEntity(EntityKind kind, Guid id, string? json)
    {
        StoryProject p = CurrentProject!;
        switch (kind)
        {
            case EntityKind.Metadata:
                if (json is not null) p.Metadata = Deserialize<StoryProjectMetadata>(json);
                break;
            case EntityKind.Container:
                if (json is null) p.ContainerNodes.Remove(id);
                else              p.ContainerNodes[id] = Deserialize<StoryContainerNode>(json);
                break;
            case EntityKind.Logic:
                if (json is null) p.LogicNodes.Remove(id);
                else              p.LogicNodes[id] = Deserialize<StoryLogicNode>(json);
                break;
            case EntityKind.Portal:
                if (json is null) p.PortalNodes.Remove(id);
                else              p.PortalNodes[id] = Deserialize<StoryPortalNode>(json);
                break;
            case EntityKind.Image:
                if (json is null) p.Images.Remove(id);
                else              p.Images[id] = Deserialize<StoryImage>(json);
                break;
            case EntityKind.Variable:
                if (json is null) p.Variables.Remove(id);
                else              p.Variables[id] = Deserialize<StoryVariable>(json);
                break;
        }
    }

    private static string Serialize<T>(T value)   => JsonConvert.SerializeObject(value, _HistoryJson);
    private static T      Deserialize<T>(string s) => JsonConvert.DeserializeObject<T>(s, _HistoryJson)!;
}
