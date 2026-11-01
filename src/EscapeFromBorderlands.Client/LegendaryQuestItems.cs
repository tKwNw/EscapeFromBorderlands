using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using EFT;
using Diz.LanguageExtensions;
using EFT.HandBook;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI.DragAndDrop;
using HarmonyLib;

namespace EscapeFromBorderlands.Client;

internal static class LegendaryQuestItems
{
    private static readonly LegendaryCatalog Catalog = LegendaryCatalog.Load();
    internal static readonly QuestItemEquivalence Identity = new(Catalog.Variants.Concat(Catalog.LegacyAliases)
        .Select(d => new KeyValuePair<string, string>(d.TemplateId, d.SourceTemplateId)));
    private static readonly MethodInfo Clone = AccessTools.Method(typeof(object), "MemberwiseClone");
    internal static bool Contains(IEnumerable<string> required, string actual) => Identity.Contains(required, actual);
    internal const string ProtectedMessage = "Legendary items cannot be handed in for quests, including items inside containers.";
    internal static bool HasProtectedItems(Item item) => Identity.IsVariant(item.StringTemplateId) ||
        item.GetAllItems().Any(child => Identity.IsVariant(child.StringTemplateId));
    internal static bool IsHandover(ConditionItem condition) => condition is ConditionHandoverItem or ConditionWeaponAssembly;
    internal static List<string> WithSources(List<string> items) => Identity.WithSources(items);

    internal static object Hit(ConditionHit condition, object value)
    {
        if (value is not HitConditionCheck hit) return value;
        // The native event is shared by multiple objectives. Only change a copy
        // of its identity data, retaining every health, distance, time and hit field.
        var copy = (HitConditionCheck)Clone.Invoke(hit, null)!;
        if (condition.weapon?.Contains(hit.Weapon) != true) copy.Weapon = Identity.Source(hit.Weapon);
        if (hit.WeaponMods != null) copy.WeaponMods = Identity.WithSources(hit.WeaponMods).ToArray();
        if (hit.TargetEquipment != null) copy.TargetEquipment = Identity.WithSources(hit.TargetEquipment);
        return copy;
    }

    internal static IEnumerable<CodeInstruction> ContainsCalls(IEnumerable<CodeInstruction> instructions)
    {
        var found = 0;
        foreach (var code in instructions)
        {
            if (code.operand is MethodInfo method && method.DeclaringType == typeof(Enumerable) && method.Name == nameof(Enumerable.Contains) &&
                method.IsGenericMethod && method.GetGenericArguments().Single() == typeof(string) && method.GetParameters().Length == 2)
            {
                code.opcode = OpCodes.Call; code.operand = AccessTools.Method(typeof(LegendaryQuestItems), nameof(Contains)); found++;
            }
            yield return code;
        }
        if (found != 1) throw new InvalidOperationException("Expected 1 quest item string comparison; found " + found);
    }
}

[HarmonyPatch(typeof(ConditionsConnectorsManager<Quest>), "ContainsPass")]
internal static class LegendaryQuestItemMatchPatch
{
    private static void Postfix(Item item, ConditionItem condition, IBaseHandbook handbook, ref bool __result)
    {
        if (LegendaryQuestItems.IsHandover(condition) && LegendaryQuestItems.HasProtectedItems(item))
        { __result = false; return; }
        if (__result) return;
        var source = LegendaryQuestItems.Identity.Source(item.StringTemplateId);
        if (source == item.StringTemplateId) return;
        __result = condition.target.Contains(source) ||
            (condition is not ConditionFindItem find || find.TargetIsCategory) && condition.target.Any(t => handbook.IsChildOf(source, t));
    }
}

[HarmonyPatch(typeof(ConditionHitProgressChecker), nameof(ConditionHitProgressChecker.Test), new[] { typeof(object) })]
internal static class LegendaryQuestHitPatch
{
    private static void Prefix(ConditionHitProgressChecker __instance, ref object testValue) => testValue = LegendaryQuestItems.Hit(__instance.Condition, testValue);
}

[HarmonyPatch(typeof(ConditionEquipmentProgressChecker), nameof(ConditionEquipmentProgressChecker.Test), new[] { typeof(object) })]
internal static class LegendaryQuestEquipmentPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var found = 0;
        foreach (var code in instructions)
        {
            yield return code;
            if (code.operand is MethodInfo method &&
                (method.DeclaringType == typeof(Inventory) && method.Name == "get_EquippedInSlotsTemplateIds" ||
                 method.DeclaringType == typeof(Enumerable) && method.Name == nameof(Enumerable.ToList) && method.IsGenericMethod && method.GetGenericArguments().Single() == typeof(string)))
            {
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(LegendaryQuestItems), nameof(LegendaryQuestItems.WithSources)));
                found++;
            }
        }
        if (found != 2) throw new InvalidOperationException("Expected 2 native equipment identity paths; found " + found);
    }
}

[HarmonyPatch(typeof(Inventory), nameof(Inventory.IsWeaponFitsCondition))]
internal static class LegendaryQuestAssemblyPatch
{
    private static void Postfix(Weapon weapon, ref bool __result)
    { if (__result && LegendaryQuestItems.HasProtectedItems(weapon)) __result = false; }
}

[HarmonyPatch]
internal static class LegendaryQuestPlacePatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("EFT.InteractionContextHelper+CG_GetAvailableActions4"), "method_0");
    private static void Postfix(Item nextItem, ref bool __result)
    { if (__result && LegendaryQuestItems.HasProtectedItems(nextItem)) __result = false; }
}

[HarmonyPatch(typeof(ItemManipulator), nameof(ItemManipulator.HandoverQuest))]
internal static class LegendaryQuestHandoverGuardPatch
{
    private static bool Prefix(Item[] items, ref OperationResult<QuestHandoverResult> __result)
    {
        if (!items.Any(LegendaryQuestItems.HasProtectedItems)) return true;
        __result = new StringError(LegendaryQuestItems.ProtectedMessage);
        return false;
    }
}

[HarmonyPatch]
internal static class LegendaryQuestPlaceCommitPatch
{
    private static readonly Type StateType = AccessTools.TypeByName("EFT.InteractionContextHelper+CG_GetAvailableActions5");
    private static readonly FieldInfo ItemField = AccessTools.Field(StateType, "resultItem");
    private static MethodBase TargetMethod() => AccessTools.Method(StateType, "method_0");
    private static void Prefix(object __instance, ref bool successful)
    {
        // Keep the native cancellation/cleanup path, including reporting failure
        // before quest progress or the inventory removal transaction can run.
        if (successful && ItemField.GetValue(__instance) is Item item && LegendaryQuestItems.HasProtectedItems(item)) successful = false;
    }
}

[HarmonyPatch(typeof(ConditionUseItemProgressChecker), nameof(ConditionUseItemProgressChecker.Test), new[] { typeof(object) })]
internal static class LegendaryQuestUsePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => LegendaryQuestItems.ContainsCalls(instructions);
}

[HarmonyPatch(typeof(ConditionalController<Quest>), "IncreaseSellToTraderCounter")]
internal static class LegendaryQuestSellPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => LegendaryQuestItems.ContainsCalls(instructions);
}

[HarmonyPatch(typeof(QuestItemViewPanel), "smethod_0")]
internal static class LegendaryQuestBadgePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => LegendaryQuestItems.ContainsCalls(instructions);
}

[HarmonyPatch]
internal static class LegendaryQuestWorldItemPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("EFT.GameWorld+CG_ManageQuestLoot"), "MoveNext");
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => LegendaryQuestItems.ContainsCalls(instructions);
}
