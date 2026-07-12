using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>Minimal PNG header reader — enough to validate an upload really is a PNG and read its pixel size.</summary>
    [PublicAPI]
    public static class PngInfo
    {
        // The 8-byte PNG signature that opens every valid file.
        private static readonly byte[] _Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>
        /// Reads the pixel size from a PNG's IHDR chunk. Returns false when <paramref name="bytes"/> is not a PNG
        /// (bad signature, too short, or the first chunk is not IHDR).
        /// </summary>
        public static bool TryReadSize(byte[]? bytes, out int width, out int height)
        {
            width = height = 0;

            // 8-byte signature + 4 length + "IHDR" + 4 width + 4 height = 24 bytes minimum.
            if (bytes is null || bytes.Length < 24) return false;

            for (int x = 0; x < _Signature.Length; ++x)
                if (bytes[x] != _Signature[x]) return false;

            // IHDR is always the first chunk; its type sits at bytes 12..15.
            if (bytes[12] != 'I' || bytes[13] != 'H' || bytes[14] != 'D' || bytes[15] != 'R') return false;

            width  = ReadBigEndianInt(bytes, 16);
            height = ReadBigEndianInt(bytes, 20);
            return width > 0 && height > 0;
        }

        /// <summary>True when <paramref name="value"/> is a positive power of two (1, 2, 4, 8, 16, …).</summary>
        public static bool IsPowerOfTwo(int value) => value > 0 && (value & (value - 1)) == 0;

        private static int ReadBigEndianInt(byte[] b, int offset) =>
            (b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3];
    }
}
