using System;

namespace DeusaldStoryCommon
{
    /// <summary>Which of the two image libraries an asset belongs to.</summary>
    public enum StoryImageKind
    {
        /// <summary>A small icon shown inline with text. Must be a square, power-of-two PNG.</summary>
        Icon,

        /// <summary>A free-size illustration referenced from story content. Any-size PNG.</summary>
        Image
    }

    /// <summary>
    /// A PNG asset stored in the project and referenced from story text by its unique <see cref="Name"/>. Two
    /// libraries exist (see <see cref="StoryImageKind"/>): inline <b>icons</b> (square, power-of-two) and free-size
    /// <b>images</b>. The pixels are held as base64 in <see cref="Data"/> so the asset fits the project's text-only
    /// "folder of files" store — one <c>Images/{guid}.json</c> file per image.
    /// </summary>
    public class StoryImage : IFileWithId
    {
        public Guid           Id     { get; set; } = Guid.NewGuid();
        public string         Name   { get; set; } = string.Empty;
        public StoryImageKind Kind   { get; set; }
        public int            Width  { get; set; }
        public int            Height { get; set; }

        /// <summary>The raw PNG bytes, base64-encoded (no <c>data:</c> URI prefix).</summary>
        public string Data { get; set; } = string.Empty;
    }
}
