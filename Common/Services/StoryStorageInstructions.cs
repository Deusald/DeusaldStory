using System;
using System.Collections.Generic;
using DeusaldLocalizerCommon;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// One player-facing storage instruction produced by a logic node's flow spine, ready for a preview to render.
    /// For the Gamebook it is a printed line (<see cref="Text"/>); for the App a player-input String instead surfaces
    /// as an input field (<see cref="IsAppInput"/>) prompted by <see cref="Text"/>. <see cref="Placement"/> decides
    /// whether it sits above or below the section text.
    /// </summary>
    public sealed class StorageInstruction
    {
        /// <summary>The printed Gamebook line, or — for an App input field — the "what to write" prompt/label.</summary>
        public string Text { get; set; } = "";

        /// <summary>App input field only — the placeholder hint shown inside the empty field (empty = a default hint).</summary>
        public string Placeholder { get; set; } = "";

        /// <summary>Where this instruction sits relative to the section text.</summary>
        public StorageInstructionPlacement Placement { get; set; }

        /// <summary>True only for a player-input String rendered in the App — draw an input field rather than a line.</summary>
        public bool IsAppInput { get; set; }

        /// <summary>App input field only — whether it accepts text or a number.</summary>
        public StringInputKind InputKind { get; set; }

        /// <summary>App input field only — the slot label the value is written to (e.g. <c>TA</c>).</summary>
        public string Slot { get; set; } = "";

        /// <summary>App input field only — largest accepted entry length in characters (0 = no limit).</summary>
        public int MaxLength { get; set; }
    }

    /// <summary>
    /// Turns a logic node's <b>Set</b> operations on its flow spine into the player-facing instructions the previews
    /// show — printed lines for the Gamebook, input fields for player-input Strings in the App — in flow order. External
    /// sets surface nothing (the game components track them); Internal sets are rendered against the global variable's
    /// slot / subtype. Operations that set no value produce nothing. Wording comes from the framework
    /// <see cref="StoryCommonLocalizationKeys"/> (with built-in English fallbacks), SmartFormatted with the slot label
    /// and any value. Host-agnostic so the App preview, Gamebook preview and PDF export share it.
    /// </summary>
    [PublicAPI]
    public static class StoryStorageInstructions
    {
        // Instruction text renders through the same path as node text, but its SmartFormat failures are already
        // reported where the author can act on them, so they are dropped here rather than duplicated per instruction.
        private static readonly List<string> _Discard = new();


        /// <summary>The ordered storage instructions for <paramref name="logic"/>'s Set operations (empty when none).</summary>
        public static List<StorageInstruction> For(
            StoryProject project, LocProject? localization, StoryLogicNode logic, StoryVariableDictionary.Context ctx)
        {
            List<StorageInstruction> list = new();
            foreach (StorySetVariableNode set in StoryLogicFlow.SetNodesInOrder(project, logic, ctx))
                if (ForSet(project, localization, logic, set, ctx) is StorageInstruction entry)
                    list.Add(entry);
            return list;
        }

        /// <summary>The player-facing instruction a single Set node surfaces for the render's medium (null when it surfaces nothing).</summary>
        public static StorageInstruction? ForSet(
            StoryProject project, LocProject? localization, StoryLogicNode logic, StorySetVariableNode set, StoryVariableDictionary.Context ctx)
        {
            StoryRenderTarget target = ctx.Target;
            StoryVariable?    v      = StoryLogicFlow.SetTarget(project, set);
            // External variables are tracked by the game components — no printed instruction, no App input field.
            if (v is null || v.Scope != StoryVariableScope.Internal) return null;

            switch (v.InternalSubtype)
            {
                case StoryInternalSubtype.Text:
                {
                    string stringVal = set.WireValue && set.StringMode == StringValueMode.Specific
                        ? StoryLogicRenderer.ResolveText(localization, set.GamebookValueText, ctx, _Discard)
                        : set.StringValue;
                    return StringEntry(localization, v.SlotIndex, set.StringMode, stringVal, set.StringInputKind,
                        set.Instruction, set.Placeholder, set.Placement, ctx, v.Name, set.MaxLength);
                }

                case StoryInternalSubtype.SmallNumber:
                {
                    if (target == StoryRenderTarget.App) return null; // stored silently in the App
                    bool             secret     = set.Secret || v.SmallNumberSource == SmallNumberSource.Token;
                    NumberValueCount valueCount = ValueCountOf(v.ValuesMap);
                    NumberStorageMode mode      = v.SmallNumberSource == SmallNumberSource.Token ? NumberStorageMode.Token : NumberStorageMode.Dice;
                    object? specificDisplay = set.WireValue && set.Assignment == NumberAssignment.SetSpecific
                        ? StoryLogicRenderer.ResolveText(localization, set.GamebookValueText, ctx, _Discard)
                        : null;
                    string? line = AssignmentLine(localization, StorageVariableType.Number, v.SlotIndex, mode, valueCount, set.Assignment, secret, set.SpecificValue, specificDisplay);
                    return Line(line, set.Placement);
                }

                case StoryInternalSubtype.BigPublicNumber:
                case StoryInternalSubtype.BigSecretNumber:
                {
                    if (target == StoryRenderTarget.App) return null;
                    bool secret = set.Secret || v.InternalSubtype == StoryInternalSubtype.BigSecretNumber;
                    object? specificDisplay = set.WireValue && set.Assignment == NumberAssignment.SetSpecific
                        ? StoryLogicRenderer.ResolveText(localization, set.GamebookValueText, ctx, _Discard)
                        : null;
                    string? line = AssignmentLine(localization, StorageVariableType.Dial, v.SlotIndex, NumberStorageMode.Dice, NumberValueCount.Six, set.Assignment, secret, set.SpecificValue, specificDisplay);
                    return Line(line, set.Placement);
                }

                default:
                    return null;
            }
        }

        /// <summary>Builds the instruction for a String slot given its value mode — the branch that splits App vs Gamebook.</summary>
        private static StorageInstruction? StringEntry(
            LocProject? localization, int slotIndex, StringValueMode mode, string stringValue,
            StringInputKind inputKind, StoryTextConfig instruction, StoryTextConfig placeholderText,
            StorageInstructionPlacement placement, StoryVariableDictionary.Context ctx, string variableName, int maxLength = 0)
        {
            StoryRenderTarget target = ctx.Target;
            string            slot   = StorageSlots.Label(StorageVariableType.String, slotIndex);

            switch (mode)
            {
                case StringValueMode.Unset:
                    return null; // slot claimed, nothing written

                case StringValueMode.Specific:
                    if (target == StoryRenderTarget.App) return null;
                    return Line(Resolve(localization, StoryCommonLocalizationKeys.StorageStringWriteSpecific, slot, value: stringValue), placement);

                case StringValueMode.PlayerInput:
                    // The instruction may inject the slot pill via {<Name>Slot}; bind it locally so an unnamed
                    // variable still resolves through the generic {slot} fallback.
                    string slotTag = PreviewHtmlSanitizer.SlotTag(slot);
                    string slotKey = StoryVariableDictionary.SlotTokenName(variableName);
                    Dictionary<string, object> slotTokens = new(StringComparer.OrdinalIgnoreCase) { ["slot"] = slotTag };
                    if (slotKey.Length > 0) slotTokens[slotKey] = slotTag;

                    string prompt = StoryLogicRenderer.ResolveText(localization, instruction, ctx, _Discard, slotTokens);
                    if (string.IsNullOrEmpty(prompt))
                        prompt = Resolve(localization, StoryCommonLocalizationKeys.StorageStringWrite, slot);

                    if (target != StoryRenderTarget.App)
                        return Line(prompt, placement);

                    string placeholder = StoryLogicRenderer.ResolveText(localization, placeholderText, ctx, _Discard);
                    return new StorageInstruction { Text = prompt, Placeholder = placeholder, Placement = placement, IsAppInput = true, InputKind = inputKind, Slot = slot, MaxLength = maxLength };

                default:
                    return null;
            }
        }

        /// <summary>Wraps a printed line in an instruction (null/empty line = no instruction).</summary>
        private static StorageInstruction? Line(string? text, StorageInstructionPlacement placement) =>
            string.IsNullOrEmpty(text) ? null : new StorageInstruction { Text = text!, Placement = placement };

        /// <summary>The number of distinct outcomes a Small Number's value map folds a D6 into (1–6).</summary>
        private static NumberValueCount ValueCountOf(SmallNumberValuesMap map) =>
            StorySmallNumberMap.Buckets(map).Count switch
            {
                <= 1 => NumberValueCount.One,
                2    => NumberValueCount.Two,
                3    => NumberValueCount.Three,
                4    => NumberValueCount.Four,
                5    => NumberValueCount.Five,
                _    => NumberValueCount.Six
            };

        /// <summary>The instruction for assigning (or clearing) a Number/Dial value on a slot.</summary>
        private static string? AssignmentLine(
            LocProject? localization, StorageVariableType type, int slotIndex, NumberStorageMode mode,
            NumberValueCount valueCount, NumberAssignment assignment, bool secret, int specificValue, object? specificDisplay = null)
        {
            string slot  = StorageSlots.Label(type, slotIndex);
            object value = specificDisplay ?? specificValue;

            if (type is StorageVariableType.Number or StorageVariableType.Dial && assignment == NumberAssignment.Unset)
                return null;

            switch (type)
            {
                case StorageVariableType.Dial:
                    return assignment == NumberAssignment.Randomize
                        ? Resolve(localization, StoryCommonLocalizationKeys.StorageDialRandom, slot)
                        : Resolve(localization, secret ? StoryCommonLocalizationKeys.StorageDialSetSecret : StoryCommonLocalizationKeys.StorageDialSet, slot, value: value);

                case StorageVariableType.Number:
                    return NumberLine(localization, slot, mode, valueCount, assignment, secret, specificValue, specificDisplay);
            }

            return null;
        }

        private static string NumberLine(
            LocProject? localization, string slot, NumberStorageMode mode, NumberValueCount valueCount,
            NumberAssignment assignment, bool secret, int specificValue, object? specificDisplay = null)
        {
            object value = specificDisplay ?? specificValue;
            int    max   = StorageSlots.ToNumber(valueCount);

            if (valueCount == NumberValueCount.One)
                return Resolve(localization, StoryCommonLocalizationKeys.StorageNumberFlagSet, slot);

            if (mode == NumberStorageMode.Dice)
            {
                if (assignment == NumberAssignment.SetSpecific)
                    return Resolve(localization, StoryCommonLocalizationKeys.StorageNumberDiceSpecific, slot, value: value);

                return max is 4 or 5
                    ? Resolve(localization, StoryCommonLocalizationKeys.StorageNumberDiceReroll, slot, max: max)
                    : Resolve(localization, StoryCommonLocalizationKeys.StorageNumberDiceFull, slot);
            }

            if (assignment == NumberAssignment.Randomize)
                return Resolve(localization, secret ? StoryCommonLocalizationKeys.StorageNumberTokenRandomSecret : StoryCommonLocalizationKeys.StorageNumberTokenRandom, slot, max: max);

            return Resolve(localization, secret ? StoryCommonLocalizationKeys.StorageNumberTokenSpecificSecret : StoryCommonLocalizationKeys.StorageNumberTokenSpecific, slot, value: value);
        }

        // The slot is injected as a <slot=NA> tag so previews render it as a styled pill; the sanitizer turns it into
        // the pill markup before display.
        private static string Resolve(LocProject? localization, Guid keyId, string slot, int? max = null, object? value = null)
        {
            Dictionary<string, object> vals = new(StringComparer.OrdinalIgnoreCase) { ["slot"] = PreviewHtmlSanitizer.SlotTag(slot) };
            if (max is int m)      vals["max"]   = m;
            if (value is not null) vals["value"] = value;
            return StoryCommonLocalizationKeys.Resolve(localization, keyId, vals);
        }
    }
}
