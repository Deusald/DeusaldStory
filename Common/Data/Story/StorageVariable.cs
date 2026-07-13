using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Which physical game component tracks a storage variable when the story is played as a printed Gamebook.
    /// Each kind has its own bank of indexed slots (see <see cref="StorageSlots"/>): Number → <c>NA, NB…</c>,
    /// Dial → <c>DA, DB…</c>, String → <c>SA, SB…</c>.
    /// </summary>
    public enum StorageVariableType
    {
        /// <summary>A D6 or number token (1–6) placed on a numbered slot.</summary>
        Number,

        /// <summary>A rotating board dial holding a value from −7 to 7, hideable behind its dot window.</summary>
        Dial,

        /// <summary>A paper sheet slot the players write free text on (e.g. a player's name).</summary>
        String
    }

    /// <summary>How a Number Storage value is physically stored.</summary>
    public enum NumberStorageMode
    {
        /// <summary>A standard D6 rolled and placed on the slot.</summary>
        Dice,

        /// <summary>A number token (blank on its back), so it can be drawn/placed as a secret.</summary>
        Token
    }

    /// <summary>How a Number/Dial value is assigned when a variable is registered or later set.</summary>
    public enum NumberAssignment
    {
        /// <summary>Claim the slot but set no value — produces no player instruction; the story can test for "not set".</summary>
        Unset,

        /// <summary>Set to a specific value chosen by the story.</summary>
        SetSpecific,

        /// <summary>Draw/roll a random value.</summary>
        Randomize
    }

    /// <summary>
    /// The bucket of distinct outcomes a Number variable represents. Fewer values are read off a full D6 by
    /// parity/band; 4 and 5 reroll out-of-range; a token draw uses tokens <c>1…N</c>.
    /// </summary>
    public enum NumberValueCount
    {
        /// <summary>A presence flag — set or unset.</summary>
        One,

        /// <summary>Two values: even / odd (bool).</summary>
        Two,

        /// <summary>Three values: 1&amp;2 / 3&amp;4 / 5&amp;6.</summary>
        Three,

        /// <summary>Four values: 1 / 2 / 3 / 4.</summary>
        Four,

        /// <summary>Five values: 1 / 2 / 3 / 4 / 5.</summary>
        Five,

        /// <summary>Six values: 1 / 2 / 3 / 4 / 5 / 6.</summary>
        Six
    }

    /// <summary>
    /// The fixed number of physical slots of each storage kind the finished game components will ship with, plus
    /// the slot-label helper. The counts are placeholders until the component design is finalized (they must
    /// become constant once the storage is 3D-printed with that many slots).
    /// </summary>
    public static class StorageSlots
    {
        public const int NumberCount = 8;
        public const int DialCount   = 4;
        public const int StringCount = 4;

        /// <summary>The number of slots available for <paramref name="type"/>.</summary>
        public static int Count(StorageVariableType type) => type switch
        {
            StorageVariableType.Number => NumberCount,
            StorageVariableType.Dial   => DialCount,
            StorageVariableType.String => StringCount,
            _                          => 0
        };

        /// <summary>The slot label prefix for <paramref name="type"/> (N/D/S).</summary>
        public static char Prefix(StorageVariableType type) => type switch
        {
            StorageVariableType.Number => 'N',
            StorageVariableType.Dial   => 'D',
            StorageVariableType.String => 'S',
            _                          => '?'
        };

        /// <summary>The player-facing label for a slot — e.g. Number index 2 → <c>"NC"</c>.</summary>
        public static string Label(StorageVariableType type, int index) =>
            $"{Prefix(type)}{(char)('A' + index)}";

        /// <summary>The numeric outcome count (1–6) a <see cref="NumberValueCount"/> stands for.</summary>
        public static int ToNumber(NumberValueCount count) => count switch
        {
            NumberValueCount.One   => 1,
            NumberValueCount.Two   => 2,
            NumberValueCount.Three => 3,
            NumberValueCount.Four  => 4,
            NumberValueCount.Five  => 5,
            NumberValueCount.Six   => 6,
            _                      => 6
        };
    }
}
