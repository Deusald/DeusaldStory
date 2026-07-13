using System;
using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    public class StoryProjectMetadata
    {
        public Guid     Id                       { get; set; } = Guid.NewGuid();
        public string   Name                     { get; set; } = string.Empty;
        public string   Slug                     { get; set; } = string.Empty;
        public string   Description              { get; set; } = string.Empty;
        public DateTime UpdatedAt                { get; set; } = DateTime.UtcNow;
        public Guid     RootStoryContainerNodeId { get; set; } = Guid.Empty;
        public int      FormatVersion            { get; set; } = 1;

        /// <summary>
        /// Storage variables (by their <see cref="StoryRegisterVariableNode.Id"/>) that are released when the story
        /// reaches The End — for variables that live through the entire story and so have no natural Unregister node.
        /// The validator treats these as unregistered at End; the printed Gamebook clears their slots there.
        /// </summary>
        public List<Guid> UnregisterAtEnd { get; set; } = new();

        /// <summary>
        /// Platform-local reference to the linked Deusald Localization project: a folder path on desktop,
        /// a "loc:" IndexedDB handle on the web. The localization project is the source of truth for the
        /// story's languages and keys — Story reads it through the shared DeusaldLocalizerCommon library.
        /// </summary>
        public string LocalizationProjectPath { get; set; } = string.Empty;
    }
}
