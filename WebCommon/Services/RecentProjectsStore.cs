using DeusaldStoryRuntime;
using Newtonsoft.Json;

namespace DeusaldStoryWeb
{
    /// <summary>
    /// Maintains the "recent projects" list shown on the home screen. Storage is abstracted via
    /// <see cref="IPreferencesStore"/> (MAUI Preferences on desktop, localStorage on the web); clearing entries.
    /// </summary>
    public sealed class RecentProjectsStore(IPreferencesStore prefs)
    {
        // Namespaced so the web client does not clobber the Localizer app's recents: both run on the same
        // origin (deusald.github.io) and share localStorage. Harmless on desktop (MAUI Preferences are sandboxed).
        private const string _RECENT_PROJECTS_KEY = "story:RecentProjects";
        private const int    _MAX_RECENT_PROJECTS = 10;

        public List<RecentProjectEntry> LoadRecentProjects()
        {
            try
            {
                string json = prefs.Get(_RECENT_PROJECTS_KEY, "[]");
                return JsonConvert.DeserializeObject<List<RecentProjectEntry>>(json) ?? new List<RecentProjectEntry>();
            }
            catch
            {
                return new List<RecentProjectEntry>();
            }
        }

        public void ClearRecentProjects()
        {
            prefs.Remove(_RECENT_PROJECTS_KEY);
        }

        /// <summary>
        /// Drops a single project from the recent list. Returns the updated list.
        /// </summary>
        public List<RecentProjectEntry> RemoveRecentProject(RecentProjectEntry entry)
        {
            List<RecentProjectEntry> projects = LoadRecentProjects();
            projects.RemoveAll(r => r.Path == entry.Path);
            prefs.Set(_RECENT_PROJECTS_KEY, JsonConvert.SerializeObject(projects));

            return projects;
        }

        public List<RecentProjectEntry> UpdateRecentProjects(StoryProject project, string location)
        {
            List<RecentProjectEntry> projects = LoadRecentProjects();

            projects.RemoveAll(r => r.Path == location);
            projects.Insert(0, new RecentProjectEntry
            {
                ProjectId   = project.Metadata.Id,
                ProjectName = project.Metadata.Name,
                Path        = location,
                NodesCount  = project.GetNumberOfNodes(),
                LocKeyCount = project.LocKeys.Count,
                LangCount   = project.Metadata.Languages.Count,
                LastEdited  = project.Metadata.UpdatedAt
            });

            if (projects.Count > _MAX_RECENT_PROJECTS)
                projects = projects.GetRange(0, _MAX_RECENT_PROJECTS);

            prefs.Set(_RECENT_PROJECTS_KEY, JsonConvert.SerializeObject(projects));
            return projects;
        }
    }

    public record RecentProjectEntry
    {
        public Guid     ProjectId   { get; init; }
        public string   ProjectName { get; init; } = "";
        public string   Path        { get; init; } = "";
        public int      NodesCount  { get; init; }
        public int      LocKeyCount { get; init; }
        public int      LangCount   { get; init; }
        public DateTime LastEdited  { get; init; } = DateTime.Now;

        public string LastEditedLabel
        {
            get
            {
                TimeSpan diff = DateTime.Now - LastEdited;
                if (diff.TotalMinutes < 2) return "just now";
                if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}m ago";
                if (diff.TotalDays < 1) return $"{(int)diff.TotalHours}h ago";
                if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
                return LastEdited.ToString("MMM d");
            }
        }
    }
}