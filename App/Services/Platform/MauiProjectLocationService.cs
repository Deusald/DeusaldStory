using CommunityToolkit.Maui.Storage;
using DeusaldStoryWeb;
using JetBrains.Annotations;

namespace App;

/// <summary>
/// Desktop <see cref="IProjectLocationService"/>: picking a save location shows the native folder picker
/// and returns the chosen folder path (the location handle for a disc-backed project).
/// </summary>
[UsedImplicitly]
public sealed class MauiProjectLocationService : IProjectLocationService
{
    public async Task<string?> PickSaveLocationAsync(string projectSlug)
    {
        FolderPickerResult result = await FolderPicker.Default.PickAsync();
        if (!result.IsSuccessful) return null;

        // Save the project into its own sub-folder named after the slug, created inside the picked folder.
        string slug = string.IsNullOrWhiteSpace(projectSlug) ? "project" : projectSlug.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
            slug = slug.Replace(invalid, '-');

        string target = Path.Combine(result.Folder.Path, slug);
        Directory.CreateDirectory(target);
        return target;
    }
}
