using DeusaldLocalizerCommon;

namespace DeusaldStoryWeb;

/// <summary>
/// Web <see cref="ILocalizationProjectPicker"/>: there is no folder dialog, so the user picks from the
/// localization projects already stored in this browser's IndexedDB. Both web apps share one database on the
/// <c>deusald.github.io</c> origin; localization projects live under the "loc:" handle prefix (Story projects
/// use "story:"). Each candidate is opened through the shared library so only valid loc projects are offered.
/// </summary>
public sealed class WebLocalizationProjectPicker(IndexedDbInterop idb, IProjectStoreFactory storeFactory)
    : ILocalizationProjectPicker
{
    // The Localizer web app mints its IndexedDB handles with this prefix (see its WebProjectLocationService).
    private const string _LOC_PREFIX = "loc:";

    public bool CanBrowse => false;

    public async Task<IReadOnlyList<LocProjectRef>> ListAsync()
    {
        List<LocProjectRef> refs = new();

        foreach (string handle in await idb.ListLocationsAsync(_LOC_PREFIX))
        {
            try
            {
                IProjectFileStore store = storeFactory.Create(handle);
                LocProject        loc   = await DeusaldLocalizerCommon.ProjectFileService.OpenAsync(store);
                refs.Add(new LocProjectRef(handle, loc.Metadata.Name, loc.Metadata.MainLanguageId, loc.Metadata.Languages.Count));
            }
            catch
            {
                // Not a valid localization project (or malformed) — skip it.
            }
        }

        return refs;
    }

    public Task<LocProjectRef?> BrowseAsync() => Task.FromResult<LocProjectRef?>(null);
}
