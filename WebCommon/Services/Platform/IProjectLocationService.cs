namespace DeusaldStoryWeb
{
    /// <summary>
    /// Provides a save <em>location</em> for a project that does not have one yet (a brand-new offline
    /// project). On desktop this shows the native folder picker and returns the chosen path; on the web it
    /// mints an IndexedDB namespace. Returns null when the user cancels.
    /// </summary>
    public interface IProjectLocationService
    {
        /// <summary>
        /// Picks a location for a new project. <paramref name="projectSlug"/> is the project's slug: on
        /// desktop the project is saved into a sub-folder of that name inside the folder the user picks.
        /// </summary>
        Task<string?> PickSaveLocationAsync(string projectSlug);
    }
}