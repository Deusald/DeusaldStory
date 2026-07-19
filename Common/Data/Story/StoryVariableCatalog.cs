using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// The one answer to "every variable selectable anywhere" — the authored catalog plus the Medium/Theme built-ins,
    /// the ChoiceA/B/C logic variables, and the read-only pseudo-variables that text maps surface as. Every dropdown,
    /// condition builder, renderer dictionary and validator resolves through here so none of them can drift apart.
    /// </summary>
    [PublicAPI]
    public static class StoryVariableCatalog
    {
        /// <summary>
        /// Every selectable variable, in a stable order: authored catalog, then Medium/Theme, then ChoiceA/B/C, then
        /// each authored variable's derived text maps.
        /// </summary>
        public static List<StoryVariable> All(StoryProject project)
        {
            List<StoryVariable> result = new(project.Variables.Values);
            result.AddRange(StoryBuiltInVariables.All);
            result.AddRange(StoryChoiceVariables.All);

            foreach (StoryVariable owner in project.Variables.Values)
                foreach (StoryVariableTextMap map in owner.TextMaps)
                    result.Add(DerivedTextMap(owner, map));

            return result;
        }

        /// <summary>
        /// Only the variables a Set node may target: authored, writable, and real (never a built-in, a Choice
        /// variable or a derived text map — those have no storage of their own to write).
        /// </summary>
        public static List<StoryVariable> Writable(StoryProject project)
        {
            List<StoryVariable> result = new();
            foreach (StoryVariable v in project.Variables.Values)
                if (!v.IsReadOnly)
                    result.Add(v);
            return result;
        }

        /// <summary>Resolves any id returned by <see cref="All"/> — authored, built-in, Choice, or a derived text map.</summary>
        public static StoryVariable? Resolve(StoryProject project, Guid id)
        {
            if (id == Guid.Empty) return null;
            if (project.Variables.TryGetValue(id, out StoryVariable? v)) return v;
            if (StoryBuiltInVariables.Find(id) is StoryVariable builtIn) return builtIn;
            if (StoryChoiceVariables.Find(id) is StoryVariable choice) return choice;
            if (ResolveTextMap(project, id) is var (owner, map)) return DerivedTextMap(owner, map);
            return null;
        }

        /// <summary>The (owner, map) pair a derived pseudo-variable id names, or null when it names something else.</summary>
        public static (StoryVariable Owner, StoryVariableTextMap Map)? ResolveTextMap(StoryProject project, Guid id)
        {
            if (id == Guid.Empty) return null;
            foreach (StoryVariable owner in project.Variables.Values)
                foreach (StoryVariableTextMap map in owner.TextMaps)
                    if (map.Id == id)
                        return (owner, map);
            return null;
        }

        /// <summary>True when <paramref name="name"/> collides with a built-in, a Choice variable or a derived text map.</summary>
        public static bool IsReservedName(StoryProject project, string name)
        {
            if (StoryBuiltInVariables.IsReservedName(name)) return true;
            if (StoryChoiceVariables.IsReservedName(name)) return true;

            foreach (StoryVariable owner in project.Variables.Values)
                foreach (StoryVariableTextMap map in owner.TextMaps)
                    if (string.Equals(StoryVariableValues.TextMapName(owner, map), name, StringComparison.OrdinalIgnoreCase))
                        return true;

            return false;
        }

        /// <summary>
        /// Materializes a text map as a read-only variable — no new storage, derived on read. Its value domain is the
        /// map's display strings, so a choice definition can enumerate it exactly like a real Options text variable.
        /// </summary>
        public static StoryVariable DerivedTextMap(StoryVariable owner, StoryVariableTextMap map) => new()
        {
            Id              = map.Id,
            Name            = StoryVariableValues.TextMapName(owner, map),
            Description     = $"Derived: \"{map.Name}\" read off {owner.Name}. Read-only.",
            Scope           = StoryVariableScope.External,
            ExternalForm    = StoryExternalForm.Runtime,
            ExternalSubtype = StoryExternalSubtype.Text,
            TextForm        = StoryTextForm.Options,
            TextOptions     = new List<string>(map.Values.Values),
            IsReadOnly      = true
        };
    }
}
