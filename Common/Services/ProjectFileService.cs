using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DeusaldLocalizerCommon;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Reads and writes a localization project stored as a "folder of files". The concrete backing store
    /// is an <see cref="DeusaldLocalizerCommon.IProjectFileStore"/> — a real disc folder on desktop/Backend, or an in-browser
    /// IndexedDB store on the web — so the exact same layout and ordering rules apply everywhere.
    ///
    /// Expected layout (paths are '/'-separated, relative to the store root):
    ///   metadata.json
    ///   Containers/           {guid}.json  per StoryContainerNode
    ///   Logic/                {guid}.json  per StoryLogicNode
    ///   Portals/              {guid}.json  per StoryPortalNode
    ///   Images/               {guid}.json  per StoryImage (base64 PNG + metadata)
    ///
    /// Every method has a <c>string folderPath</c> overload that operates on a <see cref="DeusaldLocalizerCommon.DiscProjectFileStore"/>,
    /// so existing disc callers (App, Backend) are unchanged.
    /// </summary>
    [PublicAPI]
    public static class ProjectFileService
    {
        // ── Constants ─────────────────────────────────────────────────────────

        public const string METADATA_FILE_NAME     = "metadata.json";
        public const string CONTAINERS_FOLDER      = "Containers";
        public const string LOGIC_FOLDER           = "Logic";
        public const string PORTALS_FOLDER         = "Portals";
        public const string IMAGES_FOLDER          = "Images";
        public const int    CURRENT_FORMAT_VERSION = 5;

        private static readonly JsonSerializerSettings _JsonSettings = new()
        {
            Formatting        = Formatting.Indented,
            NullValueHandling = NullValueHandling.Include,
            Converters        = { new StringEnumConverter() },
        };

        // ── Open ──────────────────────────────────────────────────────────────

        /// <summary>Opens and validates a project from a disc folder.</summary>
        public static Task<StoryProject> OpenAsync(string folderPath) =>
            OpenAsync(new DiscProjectFileStore(folderPath));

        /// <summary>
        /// Opens and validates a project from <paramref name="store"/>, returning a fully hydrated StoryProject.
        /// Throws <see cref="ProjectFolderException"/> on any structural or version error.
        /// </summary>
        public static async Task<StoryProject> OpenAsync(IProjectFileStore store)
        {
            // ── Read metadata ──────────────────────────────────────────────────
            if (!await store.FileExistsAsync(METADATA_FILE_NAME))
                throw new ProjectFolderException(
                    $"'{METADATA_FILE_NAME}' not found — this does not appear to be a valid project.");

            StoryProjectMetadata metadata = await ReadJsonAsync<StoryProjectMetadata>(store, METADATA_FILE_NAME)
                                         ?? throw new ProjectFolderException($"'{METADATA_FILE_NAME}' is empty or malformed.");

            if (metadata.FormatVersion > CURRENT_FORMAT_VERSION)
                throw new ProjectFolderException(
                    $"Project uses format version {metadata.FormatVersion} but this application " +
                    $"only supports up to version {CURRENT_FORMAT_VERSION}. Please update the application.");

            if (metadata.Id == Guid.Empty)
                throw new ProjectFolderException($"'{METADATA_FILE_NAME}' contains an invalid project Id.");

            // The linked localization project is resolved by the caller (block-until-relink on open), so a
            // story whose LocalizationProjectPath is empty or unreachable on this platform still opens here.

            // ── Read sub-folders ───────────────────────────────────────────────
            List<StoryContainerNode> containerNodes = await ReadFolderAsync<StoryContainerNode>(store, CONTAINERS_FOLDER);
            List<StoryLogicNode>     logicNodes     = await ReadFolderAsync<StoryLogicNode>(store, LOGIC_FOLDER);
            List<StoryPortalNode>    portalNodes    = await ReadFolderAsync<StoryPortalNode>(store, PORTALS_FOLDER);
            List<StoryImage>         images         = await ReadFolderAsync<StoryImage>(store, IMAGES_FOLDER);

            return new StoryProject
            {
                Metadata       = metadata,
                ContainerNodes = containerNodes.ToDictionary(k => k.Id),
                LogicNodes     = logicNodes.ToDictionary(k => k.Id),
                PortalNodes    = portalNodes.ToDictionary(k => k.Id),
                Images         = images.ToDictionary(k => k.Id)
            };
        }

        // ── Full Save ─────────────────────────────────────────────────────────

        public static Task SaveAsync(StoryProject project, string folderPath) =>
            SaveAsync(project, new DiscProjectFileStore(folderPath));

        /// <summary>
        /// Saves the entire project. Deletes files for entities that no longer exist
        /// (removed members, keys, etc.).
        /// </summary>
        public static async Task SaveAsync(StoryProject project, IProjectFileStore store)
        {
            project.Metadata.UpdatedAt     = DateTime.UtcNow;
            project.Metadata.FormatVersion = CURRENT_FORMAT_VERSION;

            await WriteJsonAsync(store, METADATA_FILE_NAME, project.Metadata);

            await SaveFolderAsync(store, CONTAINERS_FOLDER, project.ContainerNodes.Values.ToList(), n => n.Id.ToString());
            await SaveFolderAsync(store, LOGIC_FOLDER,      project.LogicNodes.Values.ToList(),     n => n.Id.ToString());
            await SaveFolderAsync(store, PORTALS_FOLDER,    project.PortalNodes.Values.ToList(),    n => n.Id.ToString());
            await SaveFolderAsync(store, IMAGES_FOLDER,     project.Images.Values.ToList(),         n => n.Id.ToString());
        }

        // ── Incremental Save ────────────────────
        public static Task SaveIncrementalAsync(StoryProject project, string folderPath, HashSet<Guid> dirtyKeyIds) =>
            SaveIncrementalAsync(project, new DiscProjectFileStore(folderPath), dirtyKeyIds);

        /// <summary>
        /// Saves the metadata and only the nodes whose Ids are in <paramref name="dirtyKeyIds"/>.
        /// Deleted nodes (present in the store but not in the project) are also removed.
        /// Use this after the user edits the story graph locally.
        /// </summary>
        public static async Task SaveIncrementalAsync(StoryProject project, IProjectFileStore store, HashSet<Guid> dirtyKeyIds)
        {
            project.Metadata.UpdatedAt = DateTime.UtcNow;

            // Always rewrite metadata (cheap, contains UpdatedAt / the localization link)
            await WriteJsonAsync(store, METADATA_FILE_NAME, project.Metadata);

            await UpdateFilesWithIdAsync(project.ContainerNodes.Values.ToList(), CONTAINERS_FOLDER);
            await UpdateFilesWithIdAsync(project.LogicNodes.Values.ToList(),     LOGIC_FOLDER);
            await UpdateFilesWithIdAsync(project.PortalNodes.Values.ToList(),    PORTALS_FOLDER);
            await UpdateFilesWithIdAsync(project.Images.Values.ToList(),         IMAGES_FOLDER);

            async Task UpdateFilesWithIdAsync<T>(List<T> data, string folder) where T : IFileWithId
            {
                // Write only dirty keys
                foreach (T key in data)
                {
                    if (!dirtyKeyIds.Contains(key.Id)) continue;
                    await WriteJsonAsync(store, $"{folder}/{key.Id}.json", key);
                }

                // Delete key files that no longer exist in the project
                HashSet<string> validFileNames = data
                                                .Select(k => $"{k.Id}.json")
                                                .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (string file in await store.ListJsonFilesAsync(folder))
                {
                    if (!validFileNames.Contains(file))
                        await store.DeleteFileAsync($"{folder}/{file}");
                }
            }
        }
        
        public static Task SaveMetadataOnlyAsync(StoryProject project, string folderPath) =>
            SaveMetadataOnlyAsync(project, new DiscProjectFileStore(folderPath));

        /// <summary>
        /// Writes only <c>metadata.json</c> (does not mint a new SyncId — the caller is expected
        /// to set <see cref="LocProjectMetadata.SyncId"/>/<see cref="LocProjectMetadata.UpdatedAt"/>
        /// explicitly). Used by the bot to stamp a new sync id in its own commit.
        /// </summary>
        public static Task SaveMetadataOnlyAsync(StoryProject project, IProjectFileStore store) =>
            WriteJsonAsync(store, METADATA_FILE_NAME, project.Metadata);
        
        private static string EntityPath(string subFolder, Guid id) => $"{subFolder}/{id}.json";
        
        // ── Folder Helpers ────────────────────────────────────────────────────

        /// <summary>
        /// Reads all *.json files from a sub-folder. Missing folders are treated
        /// as empty (project may not have any members/enums yet).
        /// </summary>
        private static async Task<List<T>> ReadFolderAsync<T>(IProjectFileStore store, string subFolder)
            where T : class
        {
            List<T> result = new List<T>();

            foreach (string file in (await store.ListJsonFilesAsync(subFolder)).OrderBy(f => f, StringComparer.Ordinal))
            {
                T? item = await ReadJsonAsync<T>(store, $"{subFolder}/{file}");
                if (item != null) result.Add(item);
            }

            return result;
        }

        /// <summary>
        /// Writes all items to a sub-folder, one file per item, and deletes files
        /// for items that are no longer in the list.
        /// </summary>
        private static async Task SaveFolderAsync<T>(
            IProjectFileStore store,
            string subFolder,
            List<T> items,
            Func<T, string> getFileName)
        {
            HashSet<string> validFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (T item in items)
            {
                string fileName = $"{getFileName(item)}.json";
                validFiles.Add(fileName);
                await WriteJsonAsync(store, $"{subFolder}/{fileName}", item);
            }

            // Remove files for deleted items
            foreach (string file in await store.ListJsonFilesAsync(subFolder))
            {
                if (!validFiles.Contains(file))
                    await store.DeleteFileAsync($"{subFolder}/{file}");
            }
        }

        // ── JSON helpers ──────────────────────────────────────────────────────

        private static async Task<T?> ReadJsonAsync<T>(IProjectFileStore store, string path) where T : class
        {
            string? json = await store.ReadTextAsync(path);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonConvert.DeserializeObject<T>(json, _JsonSettings);
        }

        private static Task WriteJsonAsync<T>(IProjectFileStore store, string path, T value)
        {
            string json = JsonConvert.SerializeObject(value, _JsonSettings);
            return store.WriteTextAsync(path, json);
        }
    }

    // ── Exception ─────────────────────────────────────────────────────────────

    /// <summary>Thrown when a project fails structural or version validation.</summary>
    public class ProjectFolderException : Exception
    {
        public ProjectFolderException(string message) : base(message) { }
    }
}