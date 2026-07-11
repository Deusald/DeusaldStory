namespace DeusaldStoryWeb
{
    /// <summary>
    /// A localization project the user can link a story to, resolved through the shared
    /// <c>DeusaldLocalizerCommon</c> library. <see cref="Reference"/> is the platform-local handle stored in
    /// the story's metadata: a folder path on desktop, a "loc:" IndexedDB handle on the web.
    /// </summary>
    public sealed record LocProjectRef(string Reference, string Name, string MainLanguageId, int LanguageCount);

    /// <summary>
    /// Platform abstraction for choosing the Deusald Localization project a story depends on. On desktop the
    /// user browses to a project folder; on the web the user picks from the localization projects already
    /// stored in this browser's IndexedDB. Implementations validate the pick by actually opening it via the
    /// shared library, so the returned <see cref="LocProjectRef"/> always points at a real loc project.
    /// </summary>
    public interface ILocalizationProjectPicker
    {
        /// <summary>Desktop: <c>true</c> (a native folder dialog). Web: <c>false</c> (pick from the stored list).</summary>
        bool CanBrowse { get; }

        /// <summary>Web: every "loc:" localization project in IndexedDB. Desktop: empty.</summary>
        Task<IReadOnlyList<LocProjectRef>> ListAsync();

        /// <summary>
        /// Desktop: open the native folder picker and validate the chosen folder. Returns <c>null</c> when the
        /// user cancels; throws when the folder is not a valid localization project so the caller can show why.
        /// Web: always <c>null</c>.
        /// </summary>
        Task<LocProjectRef?> BrowseAsync();
    }
}
