using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace EscapeFromBorderlands.Client;

internal static class BackpackRaidReport
{
    private static string Token = Guid.NewGuid().ToString("N");
    private static string Template = "";
    private static int Loot;
    internal static void BeginRaid() { Token = Guid.NewGuid().ToString("N"); Template = ""; Loot = 0; }
    internal static void Capture(BackpackRuntime.State? s)
    {
        Template = s?.Bag.StringTemplateId ?? "";
        Loot = s?.Record.Templates.Count ?? 0;
    }
    internal static void Attach(string path, ref string json)
    {
        if (!path.EndsWith("/client/match/local/end", StringComparison.Ordinal) || Template == "") return;
        try
        {
            var data = JObject.Parse(json);
            data["efbBackpack"] = new JObject { ["templateId"] = Template, ["distinctLootCount"] = Loot, ["raidToken"] = Token };
            json = data.ToString(Newtonsoft.Json.Formatting.None);
        }
        catch (Exception e) { Plugin.LogSource.LogError($"Backpack raid report could not be attached: {e.Message}"); }
    }
}

[HarmonyPatch]
internal static class BackpackRaidRequestPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var handler = AccessTools.TypeByName("SPT.Common.Http.RequestHandler")
            ?? throw new InvalidOperationException("SPT 4.1.6 RequestHandler was unavailable.");
        return handler.GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Where(m => m.Name is "PostJson" or "PostJsonAsync" or "PutJson" or "PutJsonAsync");
    }
    private static void Prefix(string path, ref string json) => BackpackRaidReport.Attach(path, ref json);
}
