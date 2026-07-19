using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// The three virtual <b>internal logic variables</b> a logic node's choice definitions write — positionally:
    /// <c>ChoiceDefinitions[0]</c> ⇒ <see cref="ChoiceA"/>, <c>[1]</c> ⇒ <see cref="ChoiceB"/>, <c>[2]</c> ⇒
    /// <see cref="ChoiceC"/>. They exist purely for App/Gamebook logic: never authored, never persisted, and never
    /// shown in the Variables panel — but always present in the SmartFormat dictionary and selectable in every
    /// condition / variable dropdown. A node's value is pinned by the <b>upstream</b> node's choice (the App
    /// navigation the player took, or the Gamebook section they turned to); it is empty at the story start.
    /// Mirrors <see cref="StoryBuiltInVariables"/> but lives in its own list so the Medium/Theme group is unaffected.
    /// </summary>
    [PublicAPI]
    public static class StoryChoiceVariables
    {
        /// <summary>How many choice definitions a logic node may carry — one per Choice variable.</summary>
        public const int MAX_DEFINITIONS = 3;

        /// <summary>Stable ids (fixed so conditions referencing them survive reloads).</summary>
        public static readonly Guid ChoiceAId = new("c401ce00-0000-4000-8000-000000000001");

        public static readonly Guid ChoiceBId = new("c401ce00-0000-4000-8000-000000000002");
        public static readonly Guid ChoiceCId = new("c401ce00-0000-4000-8000-000000000003");

        public static readonly StoryVariable ChoiceA = Make(ChoiceAId, "ChoiceA");
        public static readonly StoryVariable ChoiceB = Make(ChoiceBId, "ChoiceB");
        public static readonly StoryVariable ChoiceC = Make(ChoiceCId, "ChoiceC");

        /// <summary>All three in positional order — index x is the variable that definition x writes.</summary>
        public static readonly IReadOnlyList<StoryVariable> All = new[] { ChoiceA, ChoiceB, ChoiceC };

        public static bool IsChoiceVariable(Guid id) => id == ChoiceAId || id == ChoiceBId || id == ChoiceCId;

        public static StoryVariable? Find(Guid id) => All.FirstOrDefault(v => v.Id == id);

        /// <summary>The Choice variable written by the definition at <paramref name="index"/>; null when out of range.</summary>
        public static StoryVariable? ForIndex(int index) => index >= 0 && index < All.Count ? All[index] : null;

        /// <summary>The positional index of <paramref name="id"/>, or -1 when it names no Choice variable.</summary>
        public static int IndexOf(Guid id)
        {
            for (int x = 0; x < All.Count; ++x)
                if (All[x].Id == id)
                    return x;
            return -1;
        }

        public static bool IsReservedName(string name) =>
            All.Any(v => string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

        // The value domain is open text: a Choice may carry a number, a text option or a "from_to" range token,
        // so what a given Choice variable can hold is decided by the upstream node's definition, not fixed here.
        private static StoryVariable Make(Guid id, string name) => new()
        {
            Id              = id,
            Name            = name,
            Description     = $"Built-in: the value the upstream node's choice definition pinned into {name}. Read-only.",
            Scope           = StoryVariableScope.External,
            ExternalForm    = StoryExternalForm.Runtime,
            ExternalSubtype = StoryExternalSubtype.Text,
            TextForm        = StoryTextForm.Free,
            IsReadOnly      = true
        };
    }
}
