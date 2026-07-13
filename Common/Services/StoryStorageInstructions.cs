using System;
using System.Collections.Generic;
using DeusaldLocalizerCommon;
using JetBrains.Annotations;

namespace DeusaldStoryCommon
{
    /// <summary>
    /// Turns a logic node's storage operations (Register / Set / Unregister on its flow spine) into the player-facing
    /// instruction lines the printed Gamebook shows — "Roll a D6 and place it on slot NA", "Clear slot NA", … — in
    /// flow order. Operations that set no value (an unset registration) produce no line. Wording comes from the
    /// framework <see cref="StoryCommonLocalizationKeys"/> (with built-in English fallbacks), SmartFormatted with the
    /// slot label and any value. Host-agnostic so the App preview, Gamebook preview and PDF export share it.
    /// </summary>
    [PublicAPI]
    public static class StoryStorageInstructions
    {
        /// <summary>The ordered instruction lines for <paramref name="logic"/>'s storage operations (empty when none).</summary>
        public static List<string> For(
            StoryProject project, LocProject? localization, StoryLogicNode logic,
            StoryRenderTarget target = StoryRenderTarget.App)
        {
            List<string> lines = new();
            foreach (StorageOp op in StoryLogicFlow.StorageOps(logic, target))
            {
                string? line = op.Kind switch
                {
                    StorageOpKind.Register   => RegisterLine(localization, op.Register!),
                    StorageOpKind.Set        => SetLine(project, localization, op.Set!),
                    StorageOpKind.Unregister => ClearLine(project, localization, op.Unregister!.RegisteredVariableId),
                    _                        => null
                };
                if (!string.IsNullOrEmpty(line)) lines.Add(line!);
            }
            return lines;
        }

        private static string? RegisterLine(LocProject? localization, StoryRegisterVariableNode reg) =>
            AssignmentLine(localization, reg.Type, reg.SlotIndex, reg.Mode, reg.ValueCount, reg.Assignment, reg.Secret, reg.SpecificValue);

        private static string? SetLine(StoryProject project, LocProject? localization, StorySetVariableNode set)
        {
            StoryRegisterVariableNode? target = FindRegister(project, set.RegisteredVariableId);
            if (target is null) return null;
            return AssignmentLine(localization, target.Type, target.SlotIndex, target.Mode, target.ValueCount, set.Assignment, set.Secret, set.SpecificValue);
        }

        /// <summary>The instruction for assigning (or clearing) a value on a slot — shared by Register and Set.</summary>
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
                case StorageVariableType.String:
                    return Resolve(localization, StoryCommonLocalizationKeys.StorageStringWrite, slot);

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

        private static string Resolve(LocProject? localization, Guid keyId, string slot, int? max = null, int? value = null)
        {
            Dictionary<string, object> vals = new(StringComparer.OrdinalIgnoreCase) { ["slot"] = slot };
            if (max is int m)   vals["max"]   = m;
            if (value is int v) vals["value"] = v;
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
