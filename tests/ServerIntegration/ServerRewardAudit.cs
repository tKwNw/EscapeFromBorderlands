using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json.Nodes;
using EscapeFromBorderlands.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;

internal static class ServerRewardAudit
{
    const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    static readonly Type Rewards = typeof(RarityMod).Assembly.GetType("EscapeFromBorderlands.Server.BackpackRaidRewards", true)!;
    static readonly ConcurrentDictionary<MongoId, bool> Pending = (ConcurrentDictionary<MongoId, bool>)Rewards.GetField("PendingHealing", PrivateStatic)!.GetValue(null)!;
    static readonly MongoId First = new("aabbccddeeff001122334466"), Other = new("aabbccddeeff001122334477");
    static void Assert(bool ok, string reason) { if (!ok) throw new Exception(reason); }
    static object Child(object parent, string property)
    {
        var p = parent.GetType().GetProperty(property)!;
        var value = Activator.CreateInstance(p.PropertyType)!; p.SetValue(parent, value); return value;
    }
    static void Number(object parent, string property, int number)
    {
        var p = parent.GetType().GetProperty(property)!;
        p.SetValue(parent, Convert.ChangeType(number, Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType));
    }
    static object? Get(object parent, string property) => parent.GetType().GetProperty(property)!.GetValue(parent);
    static object MakeHealth(object character)
    {
        var health = Child(character, "Health");
        foreach (var name in new[] { "Energy", "Hydration" }) { var value = Child(health, name); Number(value, "Current", 1); Number(value, "Maximum", 100); }
        var parts = (IDictionary)Child(health, "BodyParts");
        var partType = parts.GetType().GetGenericArguments()[1]; var part = Activator.CreateInstance(partType)!;
        var hp = Child(part, "Health"); Number(hp, "Current", 0); Number(hp, "Maximum", 80);
        var effects = (IDictionary)Child(part, "Effects"); var effectType = effects.GetType().GetGenericArguments()[1];
        foreach (var key in new[] { "LightBleeding", "HeavyBleeding", "Fracture", "DestroyedPart", "Pain" }) effects.Add(key, Activator.CreateInstance(effectType));
        parts.Add("Head", part); return health;
    }
    static SptProfile Profile(MongoId id, out object pmc, out object scav)
    {
        var profile = new SptProfile(); var info = Child(profile, "ProfileInfo"); info.GetType().GetProperty("ProfileId")!.SetValue(info, id);
        var characters = Child(profile, "CharacterData"); pmc = MakeHealth(Child(characters, "PmcData")); scav = MakeHealth(Child(characters, "ScavData")); return profile;
    }
    static int Current(object health, string field) => Convert.ToInt32(Get(Get(health, field)!, "Current"));
    static void Heal(SptProfile profile) => Rewards.GetMethod("BeforeSave", PrivateStatic)!.Invoke(null, [profile]);
    internal static void Run(Action<string, string, Action> test, string configPath)
    {
        foreach (var completion in new[] { "success", "fault", "cancel" }) test("N36-" + completion, "Async raid scope survives until " + completion + " and then clears only its own profile", () => {
            Pending[First] = true; Pending[Other] = false;
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var wrapped = (Task)Rewards.GetMethod("CompleteRaid", PrivateStatic)!.Invoke(null, [First, source.Task])!;
            Assert(!wrapped.IsCompleted && Pending.ContainsKey(First), "healing scope closed before awaited save");
            var expected = new InvalidOperationException("raid fixture");
            if (completion == "success") source.SetResult(); else if (completion == "fault") source.SetException(expected); else source.SetCanceled();
            Exception? observed = null; try { wrapped.GetAwaiter().GetResult(); } catch (Exception e) { observed = e; }
            Assert(completion == "success" ? observed == null : completion == "fault" ? ReferenceEquals(observed, expected) : observed is OperationCanceledException, "original task result changed");
            Assert(!Pending.ContainsKey(First) && Pending.ContainsKey(Other), "scope leaked or another profile was cleared"); Pending.TryRemove(Other, out _);
        });
        foreach (var isPmc in new[] { true, false }) test("N37-" + (isPmc ? "pmc" : "scav"), "Save healing restores only the selected " + (isPmc ? "PMC" : "scav") + " character", () => {
            var profile = Profile(First, out var pmc, out var scav); var unrelated = Profile(Other, out var otherPmc, out _);
            Pending[First] = isPmc; Heal(unrelated); Heal(profile);
            var selected = isPmc ? pmc : scav; var untouched = isPmc ? scav : pmc;
            Assert(Current(selected, "Energy") == 100 && Current(selected, "Hydration") == 100, "resource restoration missing");
            var part = ((IDictionary)Get(selected, "BodyParts")!)["Head"]!;
            Assert(Convert.ToInt32(Get(Get(part, "Health")!, "Current")) == 80, "body HP not restored");
            var effects = (IDictionary)Get(part, "Effects")!;
            Assert(effects.Count == 1 && effects.Contains("Pain"), "injury removal changed unrelated effects");
            Assert(Current(untouched, "Energy") == 1 && Current(otherPmc, "Energy") == 1, "another character/profile was healed"); Pending.TryRemove(First, out _);
        });
        var original = File.ReadAllBytes(configPath);
        try {
            test("N38", "Config migration preserves old percentages and unrelated settings while adding backpack defaults", () => {
                File.WriteAllText(configPath, """{"pmcLegendaryWeaponChancePercent":7,"apbsPmcLegendaryChancePercent":3,"bossLegendaryChancePercent":12,"customSetting":{"keep":true}}""");
                var config = RarityConfiguration.Load();
                foreach (var (method, value) in new[] { ("standalone", 7d), ("apbs", 3d), ("bosses", 12d) })
                    Assert(config.Chance(method, "guns") == value && config.Chance(method, "melee") == value, "old chance lost");
                Assert(config.Chance("standalone", "backpacks") == 1 && config.Chance("apbs", "backpacks") == 1 && config.Chance("bosses", "backpacks") == 10, "new backpack defaults wrong");
                Assert(JsonNode.Parse(File.ReadAllText(configPath))!["customSetting"]!["keep"]!.GetValue<bool>(), "unrelated field lost");
                Assert(!File.Exists(configPath + ".efb-migration.tmp"), "migration temporary file leaked");
            });
            test("N39", "Explicit category zero survives migration and repeated loads do not rewrite config", () => {
                File.WriteAllText(configPath, """{"standalonePmcLegendaryChancePercent":9,"spawnChances":{"standalone":{"guns":0,"melee":2,"backpacks":0},"apbs":{"guns":0}}}""");
                var config = RarityConfiguration.Load(); var migrated = File.ReadAllBytes(configPath); var repeated = RarityConfiguration.Load();
                Assert(config.Chance("standalone", "guns") == 0 && config.Chance("standalone", "melee") == 2 && config.Chance("standalone", "backpacks") == 0 && repeated.Chance("apbs", "guns") == 0, "explicit category chance changed");
                Assert(migrated.SequenceEqual(File.ReadAllBytes(configPath)), "second load rewrote migrated file");
            });
        } finally { File.WriteAllBytes(configPath, original); Pending.Clear(); }
    }
}
