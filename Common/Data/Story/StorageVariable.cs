using System;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Which physical game component tracks a storage variable when the story is played as a printed Gamebook.
    /// Each kind has its own bank of indexed slots (see <see cref="StorageSlots"/>): Number → <c>NA, NB…</c>,
    /// Dial → <c>DA, DB…</c>, String → <c>TA, TB…</c>.
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

    /// <summary>
    /// Where a storage node's player-facing output sits relative to the section text — the printed-Gamebook
    /// instruction line and, in the App, any player input field. Defaults to <see cref="Below"/>.
    /// </summary>
    public enum StorageInstructionPlacement
    {
        /// <summary>After the section text (the default).</summary>
        Below,

        /// <summary>Before the section text.</summary>
        Above
    }

    /// <summary>
    /// How a Get/Set node names the storage variable it acts on — either a specific register node picked in the
    /// editor, or a <see cref="StorageVariableType"/> plus a name wired into the node's <c>Name</c> port, resolved
    /// against the registers active at that point in the flow.
    /// </summary>
    public enum StorageVariableRefMode
    {
        /// <summary>The node points at one <see cref="StoryRegisterVariableNode"/> by id (the default).</summary>
        Specific,

        /// <summary>The node picks a storage type and takes the variable's <b>name</b> from its wired <c>Name</c> (CVariable) input.</summary>
        ByType
    }

    /// <summary>Well-known operand ids used by a player-input String's App validation rule (a <see cref="StoryConditionExpr"/>).</summary>
    public static class StorageValidation
    {
        /// <summary>The rule operand standing for the value the player is entering right now (the variable being registered/set). Every other operand is a register-node id.</summary>
        public static readonly Guid ThisEntryRef = new("e0000000-0000-0000-0000-0000000000e1");
    }

    /// <summary>How a String storage variable's value is decided when it is registered or later set.</summary>
    public enum StringValueMode
    {
        /// <summary>Claim the slot but write nothing — no instruction, and no App input field.</summary>
        Unset,

        /// <summary>Bake a specific value the story author chooses; the App fills it silently (automatic).</summary>
        Specific,

        /// <summary>The player writes their own value — the App shows an input field; the Gamebook prints the wired instruction.</summary>
        PlayerInput
    }

    /// <summary>What kind of value the App input field accepts when a String is filled by the player.</summary>
    public enum StringInputKind
    {
        /// <summary>Free text.</summary>
        Text,

        /// <summary>A number.</summary>
        Number
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
        public const int NumberCount = 12;
        public const int DialCount   = 8;
        public const int StringCount = 16;

        /// <summary>The number of slots available for <paramref name="type"/>.</summary>
        public static int Count(StorageVariableType type) => type switch
        {
            StorageVariableType.Number => NumberCount,
            StorageVariableType.Dial   => DialCount,
            StorageVariableType.String => StringCount,
            _                          => 0
        };

        /// <summary>The slot label prefix for <paramref name="type"/> (N/D/T).</summary>
        public static char Prefix(StorageVariableType type) => type switch
        {
            StorageVariableType.Number => 'N',
            StorageVariableType.Dial   => 'D',
            StorageVariableType.String => 'T',
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
