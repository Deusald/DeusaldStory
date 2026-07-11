using CommunityToolkit.Maui.Storage;
using DeusaldLocalizerCommon;
using DeusaldStoryWeb;
using JetBrains.Annotations;

namespace App;

/// <summary>
/// Desktop <see cref="ILocalizationProjectPicker"/>: the user browses to a Deusald Localization project
/// folder with the native picker. The chosen folder is validated by opening it through the shared library,
/// so an invalid folder surfaces as a thrown <see cref="ProjectFolderException"/> the modal can show.
/// </summary>
[UsedImplicitly]
public sealed class MauiLocalizationProjectPicker : ILocalizationProjectPicker
{
    public bool CanBrowse => true;

    // Desktop picks by browsing, never from a list.
    public Task<IReadOnlyList<LocProjectRef>> ListAsync() =>
        Task.FromResult<IReadOnlyList<LocProjectRef>>(Array.Empty<LocProjectRef>());

    public async Task<LocProjectRef?> BrowseAsync()
    {
        FolderPickerResult result = await FolderPicker.Default.PickAsync();
        if (!result.IsSuccessful) return null; // cancelled

        string     path = result.Folder.Path;
        LocProject loc  = await DeusaldLocalizerCommon.ProjectFileService.OpenAsync(path); // throws if not a loc project
        return new LocProjectRef(path, loc.Metadata.Name, loc.Metadata.MainLanguageId, loc.Metadata.Languages.Count);
    }
}
