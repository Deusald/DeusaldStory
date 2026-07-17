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
    /// Turns a logic node's storage operations (Register / Set / Unregister on its flow spine) into the player-facing
    /// instructions the previews show — printed lines for the Gamebook, input fields for player-input Strings in the
    /// App — in flow order. Operations that set no value (unset registrations) produce nothing; the App only surfaces
    /// player-input Strings (everything else it stores silently). Wording comes from the framework
    /// <see cref="StoryCommonLocalizationKeys"/> (with built-in English fallbacks), SmartFormatted with the slot label
    /// and any value. Host-agnostic so the App preview, Gamebook preview and PDF export share it.
    /// </summary>
    [PublicAPI]
    public static class StoryStorageInstructions
    {
        /// <summary>The ordered storage instructions for <paramref name="logic"/>'s operations (empty when none).</summary>
        public static List<StorageInstruction> For(
            StoryProject project, LocProject? localization, StoryLogicNode logic,
            StoryRenderTarget target = StoryRenderTarget.App, IReadOnlyDictionary<Guid, string>? values = null)
        {
            List<StorageInstruction> list = new();
            foreach (StorageOp op in StoryLogicFlow.StorageOps(project, logic, target, values))
                if (ForOp(project, localization, logic, op, target) is StorageInstruction entry)
                    list.Add(entry);
            return list;
        }

        /// <summary>The player-facing instruction a single storage operation surfaces (null when it surfaces nothing for the medium).</summary>
        public static StorageInstruction? ForOp(
            StoryProject project, LocProject? localization, StoryLogicNode logic, StorageOp op, StoryRenderTarget target) =>
            op.Kind switch
            {
                StorageOpKind.Register   => RegisterEntry(project, localization, logic, op.Register!, target),
                // The op already resolved the target — a ByType set names it through a wire, not by id.
                StorageOpKind.Set        => SetEntry(project, localization, logic, op.Set!, op.TargetRegisterId, target),
                StorageOpKind.Unregister => ClearEntry(project, localization, op.Unregister!, target),
                _                        => null
            };

        private static StorageInstruction? RegisterEntry(
            StoryProject project, LocProject? localization, StoryLogicNode logic, StoryRegisterVariableNode reg, StoryRenderTarget target)
        {
            if (reg.Type == StorageVariableType.String)
                return StringEntry(project, localization, logic, reg.SlotIndex, reg.StringMode, reg.StringValue,
                    reg.StringInputKind, reg.InstructionIn.Id, reg.PlaceholderIn.Id, reg.Placement, target, reg.Name, reg.MaxLength);

            // Number/Dial values are stored silently in the App — only the Gamebook prints them.
            if (target == StoryRenderTarget.App) return null;
            string? line = AssignmentLine(localization, reg.Type, reg.SlotIndex, reg.Mode, reg.ValueCount, reg.Assignment, reg.Secret, reg.SpecificValue);
            return Line(line, reg.Placement);
        }

        private static StorageInstruction? SetEntry(
            StoryProject project, LocProject? localization, StoryLogicNode logic, StorySetVariableNode set,
            Guid targetRegisterId, StoryRenderTarget target)
        {
            StoryRegisterVariableNode? targetReg = FindRegister(project, targetRegisterId);
            if (targetReg is null) return null;

            if (targetReg.Type == StorageVariableType.String)
                return StringEntry(project, localization, logic, targetReg.SlotIndex, set.StringMode, set.StringValue,
                    set.StringInputKind, set.InstructionIn.Id, set.PlaceholderIn.Id, set.Placement, target, targetReg.Name, set.MaxLength);

            if (target == StoryRenderTarget.App) return null;
            string? line = AssignmentLine(localization, targetReg.Type, targetReg.SlotIndex, targetReg.Mode, targetReg.ValueCount, set.Assignment, set.Secret, set.SpecificValue);
            return Line(line, set.Placement);
        }

        private static StorageInstruction? ClearEntry(
            StoryProject project, LocProject? localization, StoryUnregisterVariableNode unreg, StoryRenderTarget target)
        {
            // A cleared slot needs no App action — the App just drops the value.
            if (target == StoryRenderTarget.App) return null;
            return Line(ClearLine(project, localization, unreg.RegisteredVariableId), unreg.Placement);
        }

        /// <summary>Builds the instruction for a String slot given its value mode — the branch that splits App vs Gamebook.</summary>
        private static StorageInstruction? StringEntry(
            StoryProject project, LocProject? localization, StoryLogicNode logic, int slotIndex, StringValueMode mode,
            string stringValue, StringInputKind inputKind, Guid instructionPortId, Guid placeholderPortId,
            StorageInstructionPlacement placement, StoryRenderTarget target, string variableName, int maxLength = 0)
        {
            string slot = StorageSlots.Label(StorageVariableType.String, slotIndex);

            switch (mode)
            {
                case StringValueMode.Unset:
                    return null; // slot claimed, nothing written

                case StringValueMode.Specific:
                    // The App fills a baked value silently; only the Gamebook instructs the player to write it.
                    if (target == StoryRenderTarget.App) return null;
                    return Line(Resolve(localization, StoryCommonLocalizationKeys.StorageStringWriteSpecific, slot, value: stringValue), placement);

                case StringValueMode.PlayerInput:
                    // The {<Name>Slot} the instruction injects renders as a styled pill, not the bare label.
                    string slotTag = PreviewHtmlSanitizer.SlotTag(slot);
                    string prompt   = StoryLogicRenderer.ResolvePortText(project, localization, logic, instructionPortId, target, slotTag, StoryLogicRenderer.SlotTokenName(variableName));
                    if (string.IsNullOrEmpty(prompt))
                        prompt = Resolve(localization, StoryCommonLocalizationKeys.StorageStringWrite, slot); // Resolve wraps the slot itself

                    if (target != StoryRenderTarget.App)
                        return Line(prompt, placement);

                    // The App draws an input field; an optional Localization-driven placeholder hints inside it (plain text).
                    string placeholder = StoryLogicRenderer.ResolvePortText(project, localization, logic, placeholderPortId, target);
                    return new StorageInstruction { Text = prompt, Placeholder = placeholder, Placement = placement, IsAppInput = true, InputKind = inputKind, Slot = slot, MaxLength = maxLength };

                default:
                    return null;
            }
        }

        /// <summary>Wraps a printed line in an instruction (null/empty line = no instruction).</summary>
        private static StorageInstruction? Line(string? text, StorageInstructionPlacement placement) =>
            string.IsNullOrEmpty(text) ? null : new StorageInstruction { Text = text!, Placement = placement };

        /// <summary>The instruction for assigning (or clearing) a Number/Dial value on a slot — shared by Register and Set.</summary>
        private static string? AssignmentLine(
            LocProject? localization, StorageVariableType type, int slotIndex, NumberStorageMode mode,
            NumberValueCount valueCount, NumberAssignment assignment, bool secret, int specificValue)
        {
            string slot = StorageSlots.Label(type, slotIndex);

            // Unset produces no instruction — the slot is claimed but left empty.
            if (type is StorageVariableType.Number or StorageVariableType.Dial && assignment == NumberAssignment.Unset)
                return null;

            switch (type)
            {
                case StorageVariableType.Dial:
                    return assignment == NumberAssignment.Randomize
                        ? Resolve(localization, StoryCommonLocalizationKeys.StorageDialRandom, slot)
                        : Resolve(localization, secret ? StoryCommonLocalizationKeys.StorageDialSetSecret : StoryCommonLocalizationKeys.StorageDialSet, slot, value: specificValue);

                case StorageVariableType.Number:
                    return NumberLine(localization, slot, mode, valueCount, assignment, secret, specificValue);
            }

            return null;
        }

        private static string NumberLine(
            LocProject? localization, string slot, NumberStorageMode mode, NumberValueCount valueCount,
            NumberAssignment assignment, bool secret, int specificValue)
        {
            int max = StorageSlots.ToNumber(valueCount);

            // A one-value presence flag is just "mark the slot as set", regardless of die/token.
            if (valueCount == NumberValueCount.One)
                return Resolve(localization, StoryCommonLocalizationKeys.StorageNumberFlagSet, slot);

            if (mode == NumberStorageMode.Dice)
            {
                if (assignment == NumberAssignment.SetSpecific)
                    return Resolve(localization, StoryCommonLocalizationKeys.StorageNumberDiceSpecific, slot, value: specificValue);

                return max is 4 or 5
                    ? Resolve(localization, StoryCommonLocalizationKeys.StorageNumberDiceReroll, slot, max: max)
                    : Resolve(localization, StoryCommonLocalizationKeys.StorageNumberDiceFull, slot);
            }

            // Token.
            if (assignment == NumberAssignment.Randomize)
                return Resolve(localization, secret ? StoryCommonLocalizationKeys.StorageNumberTokenRandomSecret : StoryCommonLocalizationKeys.StorageNumberTokenRandom, slot, max: max);

            return Resolve(localization, secret ? StoryCommonLocalizationKeys.StorageNumberTokenSpecificSecret : StoryCommonLocalizationKeys.StorageNumberTokenSpecific, slot, value: specificValue);
        }

        private static string ClearLine(StoryProject project, LocProject? localization, Guid registeredVariableId)
        {
            StoryRegisterVariableNode? target = FindRegister(project, registeredVariableId);
            string slot = target is not null ? StorageSlots.Label(target.Type, target.SlotIndex) : "?";
            return Resolve(localization, StoryCommonLocalizationKeys.StorageClearSlot, slot);
        }

        // The slot is injected as a <slot=NA> tag so previews render it as a styled pill (mirrors the String
        // player-input path); the sanitizer turns it into the pill markup before display.
        private static string Resolve(LocProject? localization, Guid keyId, string slot, int? max = null, object? value = null)
        {
            Dictionary<string, object> vals = new(StringComparer.OrdinalIgnoreCase) { ["slot"] = PreviewHtmlSanitizer.SlotTag(slot) };
            if (max is int m)          vals["max"]   = m;
            if (value is not null)     vals["value"] = value;
            return StoryCommonLocalizationKeys.Resolve(localization, keyId, vals);
        }

        private static StoryRegisterVariableNode? FindRegister(StoryProject project, Guid id)
        {
            if (id == Guid.Empty) return null;
            foreach (StoryLogicNode logic in project.LogicNodes.Values)
            {
                StoryRegisterVariableNode? found = logic.RegisterVariableNodes.Find(n => n.Id == id);
                if (found is not null) return found;
            }
            return null;
        }
    }
}
