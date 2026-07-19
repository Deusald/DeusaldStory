using System.Collections.Generic;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Whether a variable is tracked by the game's own components (<see cref="External"/>) or by the special
    /// scenario storage components — the physical board slots / dials / paper table (<see cref="Internal"/>).
    /// Both are held in memory in App mode; only the split matters when the story is played as a printed Gamebook.
    /// </summary>
    public enum StoryVariableScope
    {
        /// <summary>Tracked by game components; in the Gamebook the players themselves hold the value.</summary>
        External,

        /// <summary>Stored on the scenario's own storage components (a slot / dial / paper table entry).</summary>
        Internal
    }

    /// <summary>How much is known about an <see cref="StoryVariableScope.External"/> variable up front.</summary>
    public enum StoryExternalForm
    {
        /// <summary>Unknown until gameplay — a normal branching variable.</summary>
        Runtime,

        /// <summary>Known at App start (before the Gamebook is printed); treated as a constant from then on
        /// (e.g. the built-in Medium / Theme).</summary>
        Initial,

        /// <summary>A single author-defined value, fixed for App and Gamebook from the start (a gameplay constant).</summary>
        Constant
    }

    /// <summary>The value domain of an External variable.</summary>
    public enum StoryExternalSubtype
    {
        /// <summary>True / false.</summary>
        Bool,

        /// <summary>An integer in an author-declared inclusive range.</summary>
        Number,

        /// <summary>Text — either free-form (bounded length) or one of a fixed list of options.</summary>
        Text
    }

    /// <summary>The two shapes an External <see cref="StoryExternalSubtype.Text"/> variable can take.</summary>
    public enum StoryTextForm
    {
        /// <summary>Anything, within an author-declared min/max character count (1..30).</summary>
        Free,

        /// <summary>One of an author-declared list of options (enum-like).</summary>
        Options
    }

    /// <summary>Which scenario storage component holds an <see cref="StoryVariableScope.Internal"/> variable.</summary>
    public enum StoryInternalSubtype
    {
        /// <summary>A 1–6 value stored on a board slot as a die or a token.</summary>
        SmallNumber,

        /// <summary>A 0–20 value on a board dial, readable by all players.</summary>
        BigPublicNumber,

        /// <summary>A 0–20 value on a board dial, modified in secret (the dial's hidden side).</summary>
        BigSecretNumber,

        /// <summary>Free text written on the paper table.</summary>
        Text
    }

    /// <summary>How a <see cref="StoryInternalSubtype.SmallNumber"/> value is physically stored.</summary>
    public enum SmallNumberSource
    {
        /// <summary>A visible die (1–6) placed on the slot.</summary>
        Dice,

        /// <summary>A token placed number-down — a secret value (1–6).</summary>
        Token
    }

    /// <summary>
    /// How the raw 1–6 face of a <see cref="StoryInternalSubtype.SmallNumber"/> variable is folded into logical
    /// buckets. <see cref="StorySmallNumberMap"/> enumerates the buckets and the faces each one covers.
    /// </summary>
    public enum SmallNumberValuesMap
    {
        /// <summary>Presence only — any value means "set"; an empty slot means null.</summary>
        IsPresent,

        /// <summary>Odd ⇒ false, even ⇒ true.</summary>
        Bool,

        /// <summary>1&amp;2 ⇒ A, 3&amp;4 ⇒ B, 5&amp;6 ⇒ C.</summary>
        ThreeValues,

        /// <summary>1 ⇒ A, 2 ⇒ B, 3 ⇒ C, 4 ⇒ D (5 and 6 unused).</summary>
        FourEvenValues,

        /// <summary>1&amp;2 ⇒ A, 3&amp;4 ⇒ B, 5 ⇒ C, 6 ⇒ D.</summary>
        FourUnevenValues,

        /// <summary>1 ⇒ A, 2 ⇒ B, 3 ⇒ C, 4 ⇒ D, 5 ⇒ E (6 unused).</summary>
        FiveEvenValues,

        /// <summary>1&amp;2 ⇒ A, 3 ⇒ B, 4 ⇒ C, 5 ⇒ D, 6 ⇒ E.</summary>
        FiveUnevenValues,

        /// <summary>1 ⇒ A, 2 ⇒ B, 3 ⇒ C, 4 ⇒ D, 5 ⇒ E, 6 ⇒ F.</summary>
        SixValues
    }

    /// <summary>
    /// How long an <see cref="StoryVariableScope.Internal"/> variable holds its physical slot. A
    /// <see cref="Scenario"/> variable owns its slot for the whole scenario (no other variable may share it); a
    /// <see cref="Chapter"/> variable's slot is only occupied while inside the container nodes that declare it, so
    /// several chapter variables may reuse the same slot in different parts of the story.
    /// </summary>
    public enum StoryVariableLifespan
    {
        /// <summary>Reserved for the whole scenario — exclusive ownership of the slot.</summary>
        Scenario,

        /// <summary>Reserved only inside the containers that use it — the slot can be reused elsewhere.</summary>
        Chapter
    }

    /// <summary>One face-bucket of a <see cref="SmallNumberValuesMap"/>: a stable key, and the D6 faces it covers.</summary>
    public readonly struct SmallNumberBucket
    {
        public SmallNumberBucket(string key, IReadOnlyList<int> faces)
        {
            Key   = key;
            Faces = faces;
        }

        /// <summary>Stable key used in text-map dictionaries and rendering (e.g. "A", "True", "Present").</summary>
        public string Key { get; }

        /// <summary>The D6 faces (1–6) that fall into this bucket.</summary>
        public IReadOnlyList<int> Faces { get; }
    }

    /// <summary>
    /// The logical buckets of each <see cref="SmallNumberValuesMap"/> — the source of truth shared by the text-map
    /// editor (which rows to show) and the renderer (which bucket a face resolves to).
    /// </summary>
    public static class StorySmallNumberMap
    {
        /// <summary>The ordered buckets a <paramref name="map"/> folds a D6 into.</summary>
        public static IReadOnlyList<SmallNumberBucket> Buckets(SmallNumberValuesMap map) => map switch
        {
            SmallNumberValuesMap.IsPresent        => new[] { B("Present", 1, 2, 3, 4, 5, 6) },
            SmallNumberValuesMap.Bool             => new[] { B("False", 1, 3, 5), B("True", 2, 4, 6) },
            SmallNumberValuesMap.ThreeValues      => new[] { B("A", 1, 2), B("B", 3, 4), B("C", 5, 6) },
            SmallNumberValuesMap.FourEvenValues   => new[] { B("A", 1), B("B", 2), B("C", 3), B("D", 4) },
            SmallNumberValuesMap.FourUnevenValues => new[] { B("A", 1, 2), B("B", 3, 4), B("C", 5), B("D", 6) },
            SmallNumberValuesMap.FiveEvenValues   => new[] { B("A", 1), B("B", 2), B("C", 3), B("D", 4), B("E", 5) },
            SmallNumberValuesMap.FiveUnevenValues => new[] { B("A", 1, 2), B("B", 3), B("C", 4), B("D", 5), B("E", 6) },
            SmallNumberValuesMap.SixValues        => new[] { B("A", 1), B("B", 2), B("C", 3), B("D", 4), B("E", 5), B("F", 6) },
            _                                     => new[] { B("Present", 1, 2, 3, 4, 5, 6) }
        };

        /// <summary>The bucket a raw D6 <paramref name="face"/> resolves to under <paramref name="map"/>, or null when the face is unused.</summary>
        public static SmallNumberBucket? BucketOf(SmallNumberValuesMap map, int face)
        {
            foreach (SmallNumberBucket bucket in Buckets(map))
                foreach (int f in bucket.Faces)
                    if (f == face)
                        return bucket;
            return null;
        }

        private static SmallNumberBucket B(string key, params int[] faces) => new(key, faces);
    }

    /// <summary>
    /// A named translation of a <see cref="StoryInternalSubtype.SmallNumber"/> variable's logical buckets into
    /// display strings — e.g. a "HuntersAffiliation" map on a Bool variable with False ⇒ "Evil", True ⇒ "Good".
    /// A variable may carry several, so the same die can read as different things in different contexts.
    /// </summary>
    public class StoryVariableTextMap
    {
        public string                     Name   { get; set; } = string.Empty;

        /// <summary>Bucket key (from <see cref="StorySmallNumberMap"/>) → display string.</summary>
        public Dictionary<string, string> Values { get; set; } = new();
    }

    /// <summary>Maps a variable's storage subtype to the physical slot bank it draws from.</summary>
    public static class StoryVariableSlots
    {
        /// <summary>The slot bank an <see cref="StoryInternalSubtype"/> variable occupies (N / D / T).</summary>
        public static StorageVariableType Bank(StoryInternalSubtype subtype) => subtype switch
        {
            StoryInternalSubtype.SmallNumber     => StorageVariableType.Number,
            StoryInternalSubtype.BigPublicNumber => StorageVariableType.Dial,
            StoryInternalSubtype.BigSecretNumber => StorageVariableType.Dial,
            StoryInternalSubtype.Text            => StorageVariableType.String,
            _                                    => StorageVariableType.Number
        };

        /// <summary>The player-facing slot label for an internal variable (e.g. Small Number index 0 → "NA").</summary>
        public static string Label(StoryInternalSubtype subtype, int slotIndex) =>
            StorageSlots.Label(Bank(subtype), slotIndex);
    }
}
