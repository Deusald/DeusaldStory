using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Builds the variable state of one logic-node render — the single dictionary every SmartFormat call and every
    /// condition operand reads. Replaces the old per-SmartFormat-node "walk the wires into the Variables port" build:
    /// with variables global, every variable is simply always available.
    /// </summary>
    [PublicAPI]
    public static class StoryVariableDictionary
    {
        /// <summary>
        /// One render's live variable state. <see cref="Tokens"/> is what SmartFormat reads (name → value,
        /// case-insensitive); <see cref="ById"/> is what condition operands read (variable id → value). Both are
        /// <b>mutable during the spine walk</b> — a Constant Variable node publishes its pair when flow reaches it, a
        /// Set node updates its target, and a Randomized Instruction publishes its drawn result.
        /// </summary>
        public sealed class Context
        {
            public StoryProject      Project { get; set; } = null!;
            public StoryRenderTarget Target  { get; set; }

            /// <summary>SmartFormat placeholders: variable name → value. Case-insensitive, matching SmartFormat's lookup.</summary>
            public Dictionary<string, object> Tokens { get; } = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Condition operands: variable id → value.</summary>
            public Dictionary<Guid, string> ById { get; } = new();

            /// <summary>
            /// True when a live value was left out because the printed Gamebook cannot know it. Drives the more
            /// specific "this value isn't available in the Gamebook" hint on a SmartFormat failure.
            /// </summary>
            public bool HasOmittedVariable { get; set; }

            /// <summary>Publishes a value under both a token name and a variable id (either may be blank/empty).</summary>
            public void Set(Guid id, string token, string value)
            {
                if (!string.IsNullOrWhiteSpace(token)) Tokens[token.Trim()] = value;
                if (id != Guid.Empty) ById[id] = value;
            }

            /// <summary>A copy of <see cref="Tokens"/> with one extra binding — for a text that needs a local token (e.g. <c>{Range}</c>).</summary>
            public Dictionary<string, object> TokensWith(string name, object value)
            {
                Dictionary<string, object> copy = new(Tokens, StringComparer.OrdinalIgnoreCase) { [name] = value };
                return copy;
            }
        }

        /// <summary>
        /// Builds the dictionary for one render of <paramref name="logic"/>. It contains <b>every</b> variable the
        /// catalog knows — authored, Medium/Theme, ChoiceA/B/C and derived text maps — plus a <c>{Name}Slot</c> token
        /// per Internal variable.
        /// <para>
        /// In the <b>App</b> each entry carries its live/preview value. In the <b>Gamebook</b> an Internal variable
        /// carries its slot pill instead (the player reads the value off their own board), and a Constant/Initial
        /// External carries its fixed value. The exceptions are the entries pinned by the current section's incoming
        /// choices — those are known exactly, so they carry their concrete value in both mediums.
        /// </para>
        /// <para>
        /// A <b>runtime External</b> variable in the Gamebook is genuinely unknowable — it has no slot to point at and
        /// no fixed value — so it is deliberately left <i>out</i> of the dictionary and <see cref="Context.HasOmittedVariable"/>
        /// is set. Referencing one in Gamebook text then fails the SmartFormat render, which is what surfaces the
        /// mistake to the author rather than silently printing a blank.
        /// </para>
        /// </summary>
        public static Context Build(
            StoryProject project, StoryLogicNode logic,
            IReadOnlyDictionary<Guid, string> values, StoryRenderTarget target,
            IReadOnlyDictionary<Guid, string>? pinned = null)
        {
            Context ctx = new() { Project = project, Target = target };

            foreach (StoryVariable v in StoryVariableCatalog.All(project))
            {
                // Pinned by the incoming choice — known exactly, in either medium.
                if (pinned is not null && pinned.TryGetValue(v.Id, out string? pin))
                {
                    ctx.Set(v.Id, v.Name, pin);
                    AddSlotToken(ctx, v);
                    continue;
                }

                if (StoryBuiltInVariables.IsBuiltIn(v.Id))
                {
                    ctx.Set(v.Id, v.Name, StoryBuiltInVariables.ValueFor(v.Id, target, values));
                    continue;
                }

                // A Choice variable is only ever known by being pinned above; unpinned it is empty (story start).
                if (StoryChoiceVariables.IsChoiceVariable(v.Id))
                {
                    ctx.Set(v.Id, v.Name, values.TryGetValue(v.Id, out string? cv) ? cv : "");
                    continue;
                }

                if (StoryVariableValues.IsConstant(v))
                {
                    ctx.Set(v.Id, v.Name, v.FixedValue);
                    continue;
                }

                if (target == StoryRenderTarget.App)
                {
                    ctx.Set(v.Id, v.Name, PreviewValue(project, v, values));
                    AddSlotToken(ctx, v);
                    continue;
                }

                // ── Gamebook, live value ──
                if (v.Scope == StoryVariableScope.Internal)
                {
                    ctx.Set(v.Id, v.Name, SlotTag(v));
                    AddSlotToken(ctx, v);
                    continue;
                }

                ctx.HasOmittedVariable = true; // runtime External — unknowable on paper, left out on purpose
            }

            return ctx;
        }

        /// <summary>The <c>{Name}Slot</c> token an Internal variable's slot pill fills; a no-op for External variables.</summary>
        private static void AddSlotToken(Context ctx, StoryVariable v)
        {
            if (v.Scope != StoryVariableScope.Internal) return;
            string token = SlotTokenName(v.Name);
            if (token.Length > 0) ctx.Tokens[token] = SlotTag(v);
        }

        /// <summary>The styled slot pill for an Internal variable — its name pill when named, else the raw slot label (TA/NA/DA).</summary>
        public static string SlotTag(StoryVariable v)
        {
            if (v.Scope != StoryVariableScope.Internal) return "";
            return string.IsNullOrWhiteSpace(v.Name)
                ? PreviewHtmlSanitizer.SlotTag(StoryVariableSlots.Label(v.InternalSubtype, v.SlotIndex))
                : $"<var={v.Name}>";
        }

        /// <summary>The SmartFormat token an Internal variable's slot tag fills — its name plus a <c>Slot</c> suffix (empty when unnamed).</summary>
        public static string SlotTokenName(string variableName) =>
            string.IsNullOrWhiteSpace(variableName) ? "" : variableName.Trim() + "Slot";

        /// <summary>
        /// The App-preview value of <paramref name="v"/>: the caller-supplied live value, else a derived text map's
        /// mapped string for its owner's current bucket, else the first of its possible values.
        /// </summary>
        private static string PreviewValue(StoryProject project, StoryVariable v, IReadOnlyDictionary<Guid, string> values)
        {
            if (values.TryGetValue(v.Id, out string? val)) return val;

            // A text map has no value of its own — it reads its owner's current bucket through the map.
            if (StoryVariableCatalog.ResolveTextMap(project, v.Id) is var (owner, map))
            {
                string bucket = values.TryGetValue(owner.Id, out string? ownerValue)
                    ? ownerValue
                    : StoryVariableValues.PossibleValues(owner).FirstOrDefault() ?? "";
                return map.Values.TryGetValue(bucket, out string? mapped) ? mapped : "";
            }

            return StoryVariableValues.PossibleValues(v).FirstOrDefault() ?? "";
        }
    }
}
