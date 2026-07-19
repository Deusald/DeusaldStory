using System.Collections.Generic;
using System.Globalization;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Central helper for a global <see cref="StoryVariable"/>'s value domain — the ordered list of values it can take
    /// (for pickers and section fan-out), whether that domain is knowable before play (a constant), and the value the
    /// previews substitute. Keeps every surface (Variables panel, Get/Set modals, renderer, validator) agreeing on what
    /// a variable's possible values are.
    /// </summary>
    public static class StoryVariableValues
    {
        public const string False = "False";
        public const string True  = "True";

        /// <summary>The ordered possible values of <paramref name="v"/>, or an empty list when the domain is open (free text).</summary>
        public static List<string> PossibleValues(StoryVariable v)
        {
            List<string> result = new();
            if (v.Scope == StoryVariableScope.External)
            {
                switch (v.ExternalSubtype)
                {
                    case StoryExternalSubtype.Bool:
                        result.Add(False);
                        result.Add(True);
                        break;
                    case StoryExternalSubtype.Number:
                        for (int i = v.NumberFrom; i <= v.NumberTo && result.Count < 1000; ++i)
                            result.Add(i.ToString(CultureInfo.InvariantCulture));
                        break;
                    case StoryExternalSubtype.Text:
                        if (v.TextForm == StoryTextForm.Options)
                            result.AddRange(v.TextOptions);
                        break;
                }
                return result;
            }

            switch (v.InternalSubtype)
            {
                case StoryInternalSubtype.SmallNumber:
                    foreach (SmallNumberBucket bucket in StorySmallNumberMap.Buckets(v.ValuesMap))
                        result.Add(bucket.Key);
                    break;
                case StoryInternalSubtype.BigPublicNumber:
                case StoryInternalSubtype.BigSecretNumber:
                    for (int i = 0; i <= 20; ++i)
                        result.Add(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case StoryInternalSubtype.Text:
                    break; // open domain
            }
            return result;
        }

        /// <summary>
        /// True when <paramref name="v"/>'s value is fixed before any Gamebook section is built — a Constant or Initial
        /// External variable. Such a variable may be printed into Gamebook text; everything else is App-live/secret.
        /// </summary>
        public static bool IsConstant(StoryVariable v) =>
            v.Scope == StoryVariableScope.External &&
            v.ExternalForm is StoryExternalForm.Constant or StoryExternalForm.Initial;
    }
}
