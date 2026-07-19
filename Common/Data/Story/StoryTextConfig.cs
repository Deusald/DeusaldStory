using System;

namespace DeusaldStoryCommon
{
    /// <summary>A letter-case transform applied to a resolved text before its prefix/suffix are wrapped around it.</summary>
    public enum StoryTextCasing
    {
        None,
        Upper,
        Lower
    }

    /// <summary>
    /// One authored text: a localization key plus its formatting. The key's main-language text is always run through
    /// <see href="https://github.com/axuno/SmartFormat">SmartFormat</see> against the owning logic node's variable
    /// dictionary, then case-transformed and wrapped in the literal <see cref="Prefix"/>/<see cref="Suffix"/>.
    /// Replaces the old Localization → SmartFormat → Constant String wire chain: with variables global there is
    /// nothing to wire, so every text on a node is authored in place instead.
    /// </summary>
    public class StoryTextConfig
    {
        /// <summary>The chosen localization key's id (a <c>LocLocalizationKey.Id</c>). Empty ⇒ the text resolves to "".</summary>
        public Guid KeyId { get; set; }

        /// <summary>Letter-case transform applied to the SmartFormatted result (embedded &lt;…&gt; tags are skipped).</summary>
        public StoryTextCasing Casing { get; set; } = StoryTextCasing.None;

        /// <summary>Literal text prepended to the (case-transformed) result. Empty adds nothing.</summary>
        public string Prefix { get; set; } = string.Empty;

        /// <summary>Literal text appended after the (case-transformed) result. Empty adds nothing.</summary>
        public string Suffix { get; set; } = string.Empty;

        /// <summary>True when this text carries no key — callers skip it rather than rendering an empty block.</summary>
        public bool IsEmpty => KeyId == Guid.Empty;
    }
}
