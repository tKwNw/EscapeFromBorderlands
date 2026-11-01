using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Helpers.Quest;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Quests;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Utils;

namespace EscapeFromBorderlands.Server;

internal static class LegendaryQuestItems
{
    internal static QuestItemEquivalence Identity = new([]);
    private static bool Installed;
    internal static void Install(IEnumerable<VariantDefinition> variants, IEnumerable<LegacyAliasDefinition> aliases)
    {
        Identity = new(variants.Select(v => new KeyValuePair<string, string>(v.TemplateId, v.SourceTemplateId))
            .Concat(aliases.Select(v => new KeyValuePair<string, string>(v.TemplateId, v.SourceTemplateId))));
        if (Installed) return;
        var harmony = new Harmony("com.tkw.escapefromborderlands.server.quests");
        foreach (var type in new[] { typeof(LegendaryQuestHandoverPatch), typeof(LegendaryQuestSoldPatch), typeof(LegendaryQuestFindPatch) })
            harmony.CreateClassProcessor(type).Patch();
        Installed = true;
    }
    internal static bool Contains(List<string> required, string actual) => Identity.Contains(required, actual);
    internal static bool ProtectsHandover(PmcData profile, HandoverQuestRequestData request)
    {
        if (profile.Inventory?.Items is not { } items || request.Items is not { Count: > 0 }) return false;
        var selected = request.Items.Select(i => i.Id.ToString()).ToHashSet(StringComparer.Ordinal);
        var parents = items.ToDictionary(i => i.Id.ToString(), i => i.ParentId, StringComparer.Ordinal);
        foreach (var item in items.Where(i => Identity.IsVariant(i.Template.ToString())))
        {
            var id = item.Id.ToString();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (!string.IsNullOrEmpty(id) && seen.Add(id))
            {
                if (selected.Contains(id)) return true;
                if (!parents.TryGetValue(id, out var parent)) break;
                id = parent;
            }
        }
        return false;
    }
    internal static IEnumerable<CodeInstruction> ContainsCalls(IEnumerable<CodeInstruction> instructions)
    {
        var found = 0;
        var contains = AccessTools.Method(typeof(List<string>), nameof(List<string>.Contains));
        foreach (var code in instructions)
        {
            if (Equals(code.operand, contains))
            { code.opcode = OpCodes.Call; code.operand = AccessTools.Method(typeof(LegendaryQuestItems), nameof(Contains)); found++; }
            yield return code;
        }
        if (found != 1) throw new InvalidOperationException("Expected 1 native quest item comparison; found " + found);
    }
}

[HarmonyPatch(typeof(QuestController), nameof(QuestController.HandoverQuest))]
internal static class LegendaryQuestHandoverPatch
{
    private static readonly FieldInfo OutputField = AccessTools.Field(typeof(QuestController), "<eventOutputHolder>P");
    private static readonly FieldInfo ResponseField = AccessTools.Field(typeof(QuestController), "<httpResponseUtil>P");
    private static bool Prefix(QuestController __instance, PmcData pmcData, HandoverQuestRequestData request, MongoId sessionID, ref ItemEventRouterResponse __result)
    {
        if (!LegendaryQuestItems.ProtectsHandover(pmcData, request)) return true;
        var output = ((EventOutputHolder)OutputField.GetValue(__instance)!).GetOutput(sessionID);
        __result = ((HttpResponseUtil)ResponseField.GetValue(__instance)!).AppendErrorToOutput(output,
            "Legendary items cannot be handed in for quests, including items inside containers.");
        return false;
    }
}

[HarmonyPatch(typeof(QuestHelper), "IncrementSoldToTraderCounter")]
internal static class LegendaryQuestSoldPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => LegendaryQuestItems.ContainsCalls(instructions);
}

[HarmonyPatch]
internal static class LegendaryQuestFindPatch
{
    private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("SPTarkov.Server.Core.Helpers.Quest.QuestHelper+<>c__DisplayClass45_0"), "<GetFindItemConditionByQuestItem>b__1");
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => LegendaryQuestItems.ContainsCalls(instructions);
}
