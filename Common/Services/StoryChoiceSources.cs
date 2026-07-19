using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Everything about a Single-path node's <see cref="StoryChoiceDefinition"/>s that both the App renderer and the
    /// Gamebook expansion need: enumerating a definition into concrete entries, and finding which node's choices
    /// dimension a given node's sections.
    /// </summary>
    [PublicAPI]
    public static class StoryChoiceSources
    {
        /// <summary>
        /// One enumerated entry of a choice definition — a single button (App) / section jump (Gamebook).
        /// <see cref="KeyId"/> labels it, <see cref="ChoiceValue"/> is what lands in the definition's Choice variable,
        /// <see cref="PinnedVariableId"/> is the real variable whose value this entry also fixes (empty when the entry
        /// fixes none — a range band or a manually written option), and <see cref="RangeToken"/> is set for a range
        /// band so its label can bind <c>{Range}</c>. <see cref="FallbackText"/> labels the entry when it has no key.
        /// </summary>
        public sealed class ChoiceEntry
        {
            public ChoiceEntry(Guid keyId, string choiceValue, Guid pinnedVariableId, string? rangeToken, string fallbackText)
            {
                KeyId            = keyId;
                ChoiceValue      = choiceValue;
                PinnedVariableId = pinnedVariableId;
                RangeToken       = rangeToken;
                FallbackText     = fallbackText;
            }

            public Guid    KeyId            { get; }
            public string  ChoiceValue      { get; }
            public Guid    PinnedVariableId { get; }
            public string? RangeToken       { get; }
            public string  FallbackText     { get; }
        }

        /// <summary>
        /// Every entry of one definition, in author order. Empty when the definition is incomplete (no target
        /// variable, no ranges, no options) — the validator reports that; the renderer simply shows nothing.
        /// </summary>
        public static List<ChoiceEntry> Enumerate(StoryProject project, StoryChoiceDefinition def)
        {
            List<ChoiceEntry> result = new();

            if (def.Kind == StoryChoiceDefKind.Option)
            {
                foreach (StoryChoiceOption option in def.Options)
                {
                    if (option.FromVariable)
                    {
                        StoryVariable? target = StoryVariableCatalog.Resolve(project, option.SelectedVariableId);
                        if (target is null) continue;
                        // One entry per possible value: the option stands for "whatever this variable currently is".
                        foreach (string value in StoryVariableValues.PossibleValues(target))
                            result.Add(new ChoiceEntry(
                                option.KeyId != Guid.Empty ? option.KeyId : target.OptionConditionKeyId,
                                value, target.Id, null, value));
                    }
                    else
                    {
                        result.Add(new ChoiceEntry(option.KeyId, option.Text, Guid.Empty, null, option.Text));
                    }
                }
                return result;
            }

            StoryVariable? variable = StoryVariableCatalog.Resolve(project, def.SelectedVariableId);
            if (variable is null) return result;

            if (def.VariableMode == StoryVariableChoiceMode.RangeBased)
            {
                // A band does not determine one concrete value, so it pins only its Choice variable — the source
                // variable keeps its slot pill in the Gamebook.
                foreach (StoryChoiceRange range in def.Ranges)
                    result.Add(new ChoiceEntry(range.KeyId, range.Token, Guid.Empty, range.Token, range.Token));
                return result;
            }

            // Value based — a text map carries its own label key, otherwise the variable's.
            Guid keyId = StoryVariableCatalog.ResolveTextMap(project, def.SelectedVariableId) is var (_, map)
                ? map.ValueConditionKeyId
                : variable.ValueConditionKeyId;

            foreach (string value in StoryVariableValues.PossibleValues(variable))
                result.Add(new ChoiceEntry(keyId, value, variable.Id, null, value));

            return result;
        }

        /// <summary>
        /// The variable ids and values one entry pins, keyed for <see cref="StoryVariableDictionary.Build"/>: always
        /// the definition's own Choice variable, plus the real variable when the entry fixes one.
        /// </summary>
        public static Dictionary<Guid, string> Pins(int definitionIndex, ChoiceEntry entry)
        {
            Dictionary<Guid, string> pins = new();
            if (StoryChoiceVariables.ForIndex(definitionIndex) is StoryVariable choiceVar)
                pins[choiceVar.Id] = entry.ChoiceValue;
            if (entry.PinnedVariableId != Guid.Empty)
                pins[entry.PinnedVariableId] = entry.ChoiceValue;
            return pins;
        }

        /// <summary>
        /// One combination across all of a node's definitions — the cartesian product element that becomes one App
        /// button / Gamebook section. <see cref="Entries"/> is parallel to the node's <c>ChoiceDefinitions</c>.
        /// </summary>
        public sealed class Combination
        {
            public List<ChoiceEntry> Entries { get; } = new();

            /// <summary>Every variable this combination pins (Choice variables plus the real variables entries fix).</summary>
            public Dictionary<Guid, string> Pins { get; } = new();
        }

        /// <summary>
        /// The cartesian product of <paramref name="logic"/>'s choice definitions, capped at <paramref name="max"/>
        /// entries. <paramref name="total"/> reports the uncapped count so callers can flag truncation. A node with no
        /// definitions yields a single empty combination (it still has one continuation).
        /// </summary>
        public static List<Combination> Combinations(StoryProject project, StoryLogicNode logic, int max, out int total)
        {
            List<List<ChoiceEntry>> perDefinition = new();
            foreach (StoryChoiceDefinition def in logic.ChoiceDefinitions)
            {
                List<ChoiceEntry> entries = Enumerate(project, def);
                if (entries.Count > 0) perDefinition.Add(entries);
            }

            total = 1;
            foreach (List<ChoiceEntry> entries in perDefinition)
                total *= entries.Count;

            List<Combination> result = new() { new Combination() };
            for (int x = 0; x < perDefinition.Count; ++x)
            {
                List<Combination> next = new();
                foreach (Combination sofar in result)
                    foreach (ChoiceEntry entry in perDefinition[x])
                    {
                        if (next.Count >= max) break;
                        Combination combo = new();
                        combo.Entries.AddRange(sofar.Entries);
                        combo.Entries.Add(entry);
                        foreach (KeyValuePair<Guid, string> pin in sofar.Pins) combo.Pins[pin.Key] = pin.Value;
                        foreach (KeyValuePair<Guid, string> pin in Pins(x, entry)) combo.Pins[pin.Key] = pin.Value;
                        next.Add(combo);
                    }
                result = next;
                if (result.Count == 0) break;
            }

            if (result.Count == 0) result.Add(new Combination());
            return result;
        }

        /// <summary>
        /// Reverse index of "which Single-path node feeds this node's entry", built by resolving every Single-path
        /// node's <see cref="StoryLogicNode.SingleOut"/> <b>forwards</b> with
        /// <see cref="StoryFlowNavigator.ResolveNextLogic"/> — the same container/portal traversal playback uses.
        /// A node reached from two disagreeing sources maps to null, so an ambiguous join dimensions no sections.
        /// </summary>
        public static Dictionary<Guid, StoryLogicNode?> Build(StoryProject project)
        {
            Dictionary<Guid, StoryLogicNode?> result = new();

            foreach (StoryLogicNode source in project.LogicNodes.Values)
            {
                if (source.ExitMode != StoryLogicExitMode.SinglePath) continue;

                StoryFlowNavigator.NextLogicResult next = StoryFlowNavigator.ResolveNextLogic(project, source.SingleOut.Id);
                if (next.Kind != StoryFlowNavigator.NextKind.Logic || next.Logic is null) continue;

                if (result.TryGetValue(next.Logic.Id, out StoryLogicNode? existing))
                {
                    if (existing is null || existing.Id != source.Id) result[next.Logic.Id] = null; // disagreeing sources
                }
                else result[next.Logic.Id] = source;
            }

            return result;
        }

        /// <summary>The Single-path node whose choices dimension <paramref name="node"/>'s sections, or null.</summary>
        public static StoryLogicNode? SourceOf(StoryProject project, StoryLogicNode node) =>
            Build(project).TryGetValue(node.Id, out StoryLogicNode? source) ? source : null;
    }
}
