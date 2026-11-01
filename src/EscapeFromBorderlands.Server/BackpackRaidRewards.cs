using System.Text.Json;
using System.Collections.Concurrent;
using HarmonyLib;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;

namespace EscapeFromBorderlands.Server;

internal static class BackpackRaidRewards
{
    private static SaveServer _saves = null!;
    private static Dictionary<string, string> _bags = [];
    private static SPTarkov.Server.Core.Models.Eft.Common.ExpTable[] _experienceTable = [];
    private static readonly HashSet<string> Awarded = new(StringComparer.Ordinal);
    private static bool _installed;
    private static readonly ConcurrentDictionary<MongoId, bool> PendingHealing = new();

    internal static void Install(SaveServer saves, DatabaseService database, LegendaryCatalog catalog)
    {
        _saves = saves;
        _experienceTable = database.GetGlobals().Configuration.Exp.Level.ExperienceTable;
        _bags = catalog.Variants.Where(v => v.BackpackEffect is not null)
            .ToDictionary(v => v.TemplateId, v => v.BackpackEffect!.Key, StringComparer.Ordinal);
        if (_installed) return;
        _installed = true;
        // SPT runs this supported callback immediately before serialization.
        // Both PMC and scav raid handlers already await that native save.
        #pragma warning disable CS0618 // Supported by this 4.0.13 target; runs before profile JSON serialization.
        saves.AddBeforeSaveCallback("EscapeFromBorderlands.Backpacks", BeforeSave);
        #pragma warning restore CS0618
        new Harmony("com.tkw.escapefromborderlands.server.backpacks").Patch(
            AccessTools.Method(typeof(LocationLifecycleService), nameof(LocationLifecycleService.EndLocalRaid)),
            prefix: new HarmonyMethod(typeof(BackpackRaidRewards), nameof(Before)),
            finalizer: new HarmonyMethod(typeof(BackpackRaidRewards), nameof(Finished)));
    }

    private static PmcData? Saved(MongoId sessionId, EndLocalRaidRequestData request)
    {
        var characters = _saves.GetProfile(sessionId)?.CharacterData;
        return IsPmc(request) ? characters?.PmcData : characters?.ScavData;
    }

    private static string? Bag(EndLocalRaidRequestData request) => request.Results?.Profile?.Inventory?.Items?
        .FirstOrDefault(i => i.SlotId == "Backpack")?.Template.ToString();

    private static void Before(MongoId sessionId, EndLocalRaidRequestData request)
    {
        PendingHealing.TryRemove(sessionId, out _);
        // Normalize a successful run-through before SPT calculates survival,
        // found-in-raid retention and post-raid rewards. Deaths and transfers
        // keep their original result.
        if (request.Results?.Result == ExitStatus.RUNNER && Bag(request) is { } pass &&
            _bags.TryGetValue(pass, out var passEffect) && passEffect == "BP25-2")
            request.Results.Result = ExitStatus.SURVIVED;
        if (request.Results?.Result is ExitStatus.SURVIVED or ExitStatus.RUNNER &&
            Bag(request) is { } worn && _bags.TryGetValue(worn, out var effect) && effect == "BP36-2")
            PendingHealing[sessionId] = IsPmc(request);
        if (request.Results?.Result != ExitStatus.SURVIVED || Bag(request) is not { } bag ||
            !_bags.TryGetValue(bag, out var key) || key != "BP26-1" ||
            request.ExtensionData?.TryGetValue("efbBackpack", out var raw) != true || raw is null) return;
        var report = raw is JsonElement element ? element : JsonSerializer.SerializeToElement(raw);
        if (!report.TryGetProperty("templateId", out var template) || template.GetString() != bag ||
            !report.TryGetProperty("distinctLootCount", out var count) || !count.TryGetInt32(out var n) || n < 21 ||
            !report.TryGetProperty("raidToken", out var token) || token.GetString() is not { Length: > 0 } raidToken) return;
        var profile = request.Results.Profile;
        var oldExperience = Saved(sessionId, request)?.Info?.Experience;
        if (profile?.Info?.Experience is not { } experience || oldExperience is null) return;
        var earned = Math.Max(0, experience - oldExperience.Value);
        if (earned == 0 || !Awarded.Add(sessionId + ":" + raidToken)) return;
        if (Awarded.Count > 4096) { Awarded.Clear(); Awarded.Add(sessionId + ":" + raidToken); }
        profile.Info.Experience = (int)Math.Min(int.MaxValue, experience + Math.Floor(earned * .5d));
        profile.Info.Level = profile.CalculateLevel(_experienceTable);
    }

    private static bool IsPmc(EndLocalRaidRequestData request) =>
        request.ServerId?.Split('.').ElementAtOrDefault(1)?.Contains("pmc", StringComparison.OrdinalIgnoreCase) == true;

    private static Exception? Finished(MongoId sessionId, Exception? __exception)
    {
        PendingHealing.TryRemove(sessionId, out _); return __exception;
    }

    internal static SptProfile BeforeSave(SptProfile profile)
    {
        if (profile.ProfileInfo?.ProfileId is not { } id || !PendingHealing.TryGetValue(id, out var pmc)) return profile;
        Heal(pmc ? profile.CharacterData?.PmcData : profile.CharacterData?.ScavData);
        return profile;
    }

    private static void Heal(PmcData? profile)
    {
        if (profile?.Health is not { } health) return;
        if (health.Energy is { } energy) energy.Current = energy.Maximum;
        if (health.Hydration is { } hydration) hydration.Current = hydration.Maximum;
        foreach (var part in health.BodyParts?.Values.AsEnumerable() ?? [])
        {
            if (part.Health is { } hp) hp.Current = hp.Maximum;
            if (part.Effects is not { } effects) continue;
            foreach (var effect in effects.Keys.Where(e => e is "LightBleeding" or "HeavyBleeding" or "Fracture" or "DestroyedPart").ToArray()) effects.Remove(effect);
        }
    }
}
