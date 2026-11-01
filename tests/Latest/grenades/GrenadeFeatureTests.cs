using System.Reflection;
using System.Reflection.Emit;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EscapeFromBorderlands.Client;
using HarmonyLib;
using UnityEngine;
using S = EscapeFromBorderlands.Server;

internal static partial class GrenadeFeatureTests
{
    private static readonly Dictionary<string, VariantDefinition> Designs = LegendaryCatalog.Load().Variants.Where(v => v.GrenadeChargesPerRaid > 0).ToDictionary(v => v.RuntimeKey);
    private static ThrowWeap Copy(string key, string? id = null) => new() { Id = id ?? Guid.NewGuid().ToString("N")[..24], StringTemplateId = Designs[key].TemplateId };
    private static Player Scene(params ThrowWeap[] items)
    {
        Time.time = 100; Grenade.Blasts.Clear(); Physics.Blocked = false; Physics.Ignored.Clear(); UnityEngine.Object.All.Clear();
        AdvancedLegendaryActions.Smokes.Clear(); LegendaryGrenadeSettings.Reset();
        GrenadeFactory.RunCreatedHook=true;
        Singleton<ItemFactory>.Instance = new(); Singleton<EFT.AssetsManager.ObjectsFactory>.Instance = new();
        Singleton<GlobalEventDispatcher>.Instance = new(); Singleton<Systems.Effects.Effects>.Instance = new();
        var p = new Player(); Singleton<GameWorld>.Instance = new() { MainPlayer = p };
        p.InventoryController.Reachable.AddRange(items); ReusableGrenadeRuntime.FactoryHookReady = true;
        return p;
    }
    private static Grenade Throw(Player p, ThrowWeap item)
    {
        var hands = new Player.BaseGrenadeHandsController { Owner = p, Item = item };
        ReusableGrenadeRuntime.Release(hands, 0, new(), Quaternion.identity, new(), false);
        return hands.Projectiles.Single();
    }
    private static float Hit(Player target, Player? shooter, Item weapon, bool blast = true, EBodyPart part = EBodyPart.Chest, bool blocked = false, float amount = 20f, EDamageType? type = null)
    {
        var info = new DamageInfo { DamageType = type ?? (blast ? EDamageType.GrenadeFragment : EDamageType.Bullet), Weapon = weapon, SourceId = blast ? weapon.StringTemplateId : "fragment-template", MasterOrigin = Vector3.zero,
            Player = shooter == null ? null : new Bridge(shooter), Blunt = blocked, Damage = amount };
        var health = target.ActiveHealthController;
        object?[] args = [health, part, amount, info, null]; PatchHooks.Prefix("LegendaryGrenadeDamagePatch", args);
        var state = args[4]!;
        try
        {
            var damage = (float)args[2]!;
            health.Parts[part].Current -= damage;
            typeof(LegendaryGrenadeDamagePatch).GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [health, part, state]);
            return damage;
        }
        finally { typeof(LegendaryGrenadeDamagePatch).GetMethod("Finalizer", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [state, null]); }
    }
    private static void Flash(Grenade grenade, params (Player Player, float Distance, bool Facing)[] targets)
    {
        var before = LegendaryGrenadeEffects.CurrentExplosion;
        LegendaryGrenadeEffects.Try(grenade.WeaponSource, out var payload); LegendaryGrenadeEffects.CurrentExplosion = payload;
        var effect = ReusableGrenadeRuntime.Explosion(grenade.WeaponSource, grenade.WeaponSource);
        try
        {
            var bridges = targets.Select(t => (IObserverToPlayerBridge)new Bridge(t.Player)).ToList();
            var caches = bridges.Select((b, i) => (b, i)).ToDictionary(v => v.b, v => new PlayerEmitterCache { Distance = targets[v.i].Distance,
                DirectionToEmitter = targets[v.i].Facing ? new(0, 0, 1) : new(0, 0, -1), TryToApplyStun = true, TryToApplyBurnEyes = true });
            FirearmControllerSharedMethods.ApplyLightAndSoundHealthEffects(bridges, caches, grenade.transform.position, effect.Blindness);
        }
        finally { LegendaryGrenadeEffects.CurrentExplosion = before; }
    }
    private static float RoundDelay(object process)
    {
        var patch=typeof(ReusableGrenadeRuntime).Assembly.GetType("EscapeFromBorderlands.Client.GrenadePackingDelayPatch");
        if(patch==null)return Native.Number(process,"_cooldown");
        object?[] args=[process,null];patch.GetMethod("Prefix",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,args);
        try{return Native.Number(process,"_cooldown");}
        finally{patch.GetMethod("Finalizer",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[process,args[1],null]);}
    }
    internal static void Run(Action<string, Action> run, Action<bool, string> check, Action<float, float, string> near)
    {
        run("Approved catalog contains exactly 37 designs, charge exceptions and unchanged Encore identity", () =>
        {
            check(Designs.Count == 37 && Designs.Values.Select(v => v.SourceTemplateId).Distinct().Count() == 12, "37 designs / 12 base types");
            check(Designs["GR26"].TemplateId == "8f4c0ee935f992ed790b041f", "saved Encore identity");
            check(Designs["GR15"].GrenadeChargesPerRaid == 1 && Designs["GR15"].GrenadeRechargeSeconds == 300, "Big One exception");
            check(Designs["GR36"].GrenadeChargesPerRaid == 5 && Designs["GR40"].GrenadeChargesPerRaid == 15, "5 and 15 charges");
            check(!Designs.Keys.Any(k => int.Parse(k[2..]) is >= 41 and <= 45) && !Designs.ContainsKey("GR22"), "event variants and dedicated breacher excluded");
            var server = S.LegendaryCatalog.Load().Variants.Where(v => v.GrenadeChargesPerRaid > 0).ToDictionary(v => v.RuntimeKey);
            foreach (var d in Designs.Values) check(server[d.RuntimeKey].TemplateId == d.TemplateId && server[d.RuntimeKey].EffectText == d.EffectText && server[d.RuntimeKey].GrenadeCategory == d.GrenadeCategory, "matching client/server definition " + d.RuntimeKey);
        });
        run("Each design rolls exactly 4 compatible, distinct, stable random bonuses", () =>
        {
            foreach (var design in Designs.Values)
            for (var n = 0; n < 200; n++)
            {
                var item = Copy(design.RuntimeKey, design.RuntimeKey + "-" + n); var r = RarityResolver.Resolve(item)!;
                check(r.AffixDescriptions.Count == 5 && r.AffixDescriptions.Skip(1).Distinct().Count() == 4, "signature and 4 distinct bonuses");
                if (design.GrenadeCategory != "fragmentation") check(r.GrenadeDamageMultiplier == 1 && r.GrenadeFragmentMultiplier == 1 && !r.GrenadeDoorBreach, "no damaging bonuses on flash/smoke");
                if (design.GrenadeCategory != "flash") check(r.GrenadeFlashDurationMultiplier == 1 && r.GrenadeFlashRadiusMultiplier == 1, "no flash rolls on other types");
                if (design.GrenadeCategory != "smoke") check(r.GrenadeSmokeDurationMultiplier == 1 && r.GrenadeSmokeSizeMultiplier == 1 && r.GrenadeSmokeBuildTimeMultiplier == 1, "no smoke rolls on other types");
                var text = string.Join('|', r.AffixDescriptions); RarityResolver.ResetSession();
                check(string.Join('|', RarityResolver.Resolve(item)!.AffixDescriptions) == text, "same physical ID retains roll");
            }
        });
        run("Recharge starts on successful spending and does not reset on another throw", () =>
        {
            var item = Copy("GR26"); var p = Scene(item); Throw(p, item); Time.time = 200; Throw(p, item);
            near((float)ReusableGrenadeRuntime.Charges.SecondsUntil(item.Id, 3), 80, "original deadline retained");
            Time.time = 279.999f; check(ReusableGrenadeRuntime.Remaining(item) == 1, "no early restore");
            Time.time = 280; check(ReusableGrenadeRuntime.Remaining(item) == 2, "1 charge at exact deadline");
            Time.time = 460; check(ReusableGrenadeRuntime.Remaining(item) == 3, "next interval fills copy");
            check(ReusableGrenadeRuntime.Charges.SecondsUntil(item.Id, 3) == 0, "clock stops full");
        });
        run("Independent deadlines survive stowing, drop/pickup, hidden HUD and long catch-up", () =>
        {
            var a = Copy("GR26"); var b = Copy("GR26"); var p = Scene(a, b); Throw(p, a); Time.time = 150; Throw(p, b);
            p.InventoryController.Reachable.Clear(); Time.time = 280;
            check(ReusableGrenadeRuntime.Remaining(a) == 3 && ReusableGrenadeRuntime.Remaining(b) == 2, "per-copy timing while dropped");
            p.InventoryController.Reachable.Add(b); Throw(p, b); Throw(p, b);
            check(!ReusableGrenadeRuntime.Usable(b), "depleted copy filtered");
            Time.time = 330; check(ReusableGrenadeRuntime.Usable(b) && ReusableGrenadeRuntime.Remaining(b) == 1, "automatically available after restore");
            Time.time = 10000; check(ReusableGrenadeRuntime.Remaining(b) == 3 && !ReusableGrenadeRuntime.Statuses(p).Any(), "catch up to full without HUD polling");
        });
        run("Failed throws and reservations never start recharge; refill starts a fresh clock", () =>
        {
            double now = 0; var ledger = new GrenadeChargeLedger(() => now);
            check(ledger.Reserve("x", 1, "fail") && ledger.Finish("x", "fail", false), "cancelled reservation");
            check(ledger.SecondsUntil("x", 1) == 0, "failed release no countdown");
            ledger.Reserve("x", 1, "ok"); ledger.Finish("x", "ok", true); now = 180;
            check(ledger.Remaining("x", 1) == 1, "refills at boundary"); now = 1000;
            ledger.Reserve("x", 1, "next"); ledger.Finish("x", "next", true);
            check(ledger.SecondsUntil("x", 1) == 180, "new spend receives a complete interval");
        });
        run("Holy Hand Grenade uses 300 seconds while Small Claims and Shagbag retain 180", () =>
        {
            var big = Copy("GR15"); var small = Copy("GR36"); var shag = Copy("GR40"); var p = Scene(big, small, shag);
            foreach (var i in new[] { big, small, shag }) Throw(p, i);
            Time.time = 280; check(ReusableGrenadeRuntime.Remaining(big) == 0 && ReusableGrenadeRuntime.Remaining(small) == 5 && ReusableGrenadeRuntime.Remaining(shag) == 15, "separate capacity/timing");
            Time.time = 400; check(ReusableGrenadeRuntime.Remaining(big) == 1, "Big One restores after 5 minutes");
        });
        run("HUD full, held, stowed, empty and 2:45 examples reflect the live ledger", () =>
        {
            var item = Copy("GR26"); var p = Scene(item);
            check(!ReusableGrenadeRuntime.Statuses(p).Any(), "full stowed hidden");
            p.HandsController = new Player.BaseGrenadeHandsController { Item = item, Owner = p };
            check(ReusableGrenadeRuntime.Statuses(p).Single().Label == "Encore 3/3 charges", "full held counter");
            Throw(p, item); Time.time += 15;
            check(ReusableGrenadeRuntime.Statuses(p).Single().Label == "Encore 2/3 charges · +1 charge in 2:45", "held combined status");
            p.HandsController = new(); check(ReusableGrenadeRuntime.Statuses(p).Single().Label == "Encore 2/3 charges · +1 charge in 2:45", "stowed partial count and cooldown");
            Throw(p, item); Throw(p, item);
            check(!ReusableGrenadeRuntime.Usable(item) && !ReusableGrenadeRuntime.Statuses(p).Single().Label.Contains("0/3"), "empty cooldown without counter");
            check(!StatusVisibility.Allows("grenade:a", true, true, true, true, false) && !StatusVisibility.Allows("grenade-buff:reload", true, true, true, true, false), "grenade visibility governs both kinds");
        });
        run("Semtax adheres to its first surface and follows moving and rotating scenery", () =>
        {
            var item = Copy("GR04"); var p = Scene(item); var grenade = Throw(p, item);
            grenade.transform.position = new(2, 0, 0); var first = new Component(); var second = new Component();
            check(!LegendaryGrenadeEffects.Collision(grenade, new Collision { collider = first }) && grenade.Rigidbody.isKinematic, "first collision stops native bounce and impact path");
            first.transform.position = new(5, 0, 0); first.transform.rotation = new(0, 0, MathF.Sqrt(.5f), MathF.Sqrt(.5f)); grenade.LateUpdate();
            near(grenade.transform.position.x, 5, "follows translation"); near(grenade.transform.position.y, 2, "follows rotation");
            LegendaryGrenadeEffects.Collision(grenade, new Collision { collider = second }); first.transform.position = new(6, 0, 0); grenade.LateUpdate();
            near(grenade.transform.position.x, 6, "later contacts cannot replace anchor");
            near(grenade.WeaponSource.Template.ExplDelay, 3, "unchanged running fuse");
        });
        run("Semtax can attach to enemies; ordinary grenades keep native collision handling", () =>
        {
            var item = Copy("GR04"); var p = Scene(item); var enemy = new Player(); Singleton<GameWorld>.Instance.Actors.Add(enemy);
            var body = new BodyPartCollider { PlayerProfileID = enemy.ProfileId }; body.gameObject.Components.Add(body);
            var grenade = Throw(p, item); check(!LegendaryGrenadeEffects.Collision(grenade, new Collision { collider = body }), "enemy contact accepted");
            body.transform.position = new(3, 0, 0); grenade.LateUpdate(); near(grenade.transform.position.x, 3, "follows enemy bone");
            var ordinary = new Grenade { Original = new() }; check(LegendaryGrenadeEffects.Collision(ordinary, new Collision()), "ordinary collision unchanged");
        });
        run("Encore repeats 1 full explosion after exactly 1 second without another charge", () =>
        {
            var item = Copy("GR26"); var p = Scene(item); var grenade = Throw(p, item); var at = new Vector3(3, 4, 5); grenade.transform.position = at; grenade.InvokeBlowUpEvent();
            check(Grenade.Blasts.Count == 1 && ReusableGrenadeRuntime.Remaining(item) == 2, "primary spends only 1 charge");
            Time.time = 100.999f; Singleton<GameWorld>.Instance.Tick(); check(Grenade.Blasts.Count == 1, "not early");
            Time.time = 101; Singleton<GameWorld>.Instance.Tick(); check(Grenade.Blasts.Count == 2, "one delayed full blast");
            var a = Grenade.Blasts[0]; var b = Grenade.Blasts[1]; check(a.Position == b.Position && b.Position == at && a.Item.Id != b.Item.Id, "same location, new projectile identity");
            near(a.Effect.GetStrength, b.Effect.GetStrength, "full repeated blast"); check(a.Effect.FragmentsCount == b.Effect.FragmentsCount, "full repeated fragment count");
            Time.time = 105; Singleton<GameWorld>.Instance.Tick(); check(Grenade.Blasts.Count == 2 && ReusableGrenadeRuntime.Remaining(item) == 2, "no recursive repeats or extra charge");
        });
        run("M.I.R.V. creates exactly 3 normal F-1 grenades with separate IDs and no legendary inheritance", () =>
        {
            var item = Copy("GR13"); var p = Scene(item); var grenade = Throw(p, item); grenade.InvokeBlowUpEvent();
            var children = Singleton<GameWorld>.Instance.Grenades.Where(g => g != grenade).ToArray();
            check(children.Length == 3 && children.All(g => g.WeaponSource.StringTemplateId == "5710c24ad2720bc3458b45a3"), "3 F-1 children");
            check(children.Select(g => g.WeaponSource.Id).Distinct().Count() == 3 && children.All(g => !LegendaryGrenadeEffects.Try(g.WeaponSource, out _)), "ordinary identities without payloads");
            foreach (var c in children) { near(c.WeaponSource.Template.ExplDelay, 3, "normal F-1 fuse"); var v = c.Rigidbody.velocity; var distance = MathF.Sqrt(v.x * v.x + v.z * v.z) * 2 * v.y / 9.81f; check(distance >= 5 && distance <= 10, "5–10 m nominal scatter"); c.InvokeBlowUpEvent(); }
            check(Singleton<GameWorld>.Instance.Grenades.Count == 4 && ReusableGrenadeRuntime.Remaining(item) == 2, "no recursion, inventories or extra charge use");
        });
        run("Raid cleanup cancels pending secondary explosions", () =>
        {
            var item = Copy("GR26"); var p = Scene(item); Throw(p, item).InvokeBlowUpEvent(); ReusableGrenadeRuntime.Reset(); Time.time = 101;
            Singleton<GameWorld>.Instance.Tick(); check(Grenade.Blasts.Count == 1, "pending previous-session repeat discarded");
        });
        run("Side Business adds an immediate 15-second smoke cloud exactly once", () =>
        {
            var item = Copy("GR12"); var p = Scene(item); var g = Throw(p, item); g.InvokeBlowUpEvent(); LegendaryGrenadeEffects.Detonated(g, g.WeaponSource, g.transform.position);
            check(AdvancedLegendaryActions.Smokes.Count == 1 && AdvancedLegendaryActions.Smokes[0].Seconds == 15 && AdvancedLegendaryActions.Smokes[0].Instant, "one immediate cloud");
        });
        run("Holy Hand Grenade has a fixed 3-second projectile fuse", () =>
        {
            var item = Copy("GR15"); var p = Scene(item); var grenade = Throw(p, item); near(grenade.WeaponSource.Template.ExplDelay, 3, "fixed 3-second legendary fuse");
            near(ItemFactory.Template(Designs["GR15"].SourceTemplateId).ExplDelay, 5, "vanilla template unchanged");
        });
        run("Street Sweeper, Little Lemon, Baseball, Big Bang and Shagbag apply their approved static modifiers", () =>
        {
            foreach (var (key, fragments, radius, distance) in new[] { ("GR02", 1.75f, 1f, 1f), ("GR06", 2f, 1f, 1f), ("GR14", 1f, 1.5f, 2f), ("GR35", 1f, 1.3f, 1f), ("GR40", 1f, 1f, 1.5f) })
            {
                var copy = Copy(key); var roll = RarityResolver.Resolve(copy)!;
                check(roll.GrenadeFragmentMultiplier >= fragments && roll.GrenadeRadiusMultiplier >= radius && roll.GrenadeThrowDistanceMultiplier >= distance, "approved base multipliers " + key);
            }
        });
        run("Danger Zone, Ankle Biter and Point Blank boost only their intended blast targets", () =>
        {
            foreach (var key in new[] { "GR01", "GR24", "GR27" })
            {
                var item = Copy(key); var p = Scene(item); var target = new Player { Position = new(3, 0, 0) }; Singleton<GameWorld>.Instance.Actors.Add(target); var g = Throw(p, item);
                if (key == "GR27") LegendaryGrenadeEffects.Collision(g, new Collision { collider = new BodyPartCollider { PlayerProfileID = target.ProfileId } });
                var boosted = Hit(target, p, g.WeaponSource, part: key == "GR24" ? EBodyPart.LeftLeg : EBodyPart.Chest);
                near(boosted, key == "GR27" ? 40 : 35, "signature blast bonus " + key);
                near(Hit(target, p, g.WeaponSource, blast: false), 20, "fragment damage not boosted by blast signature");
                if (key == "GR01") { target.Position = new(3.01f, 0, 0); near(Hit(target, p, g.WeaponSource), 20, "outside 3 m"); }
                if (key == "GR24") near(Hit(target, p, g.WeaponSource), 20, "non-leg blast unmodified");
            }
        });
        run("Small Claims snapshots pistol-only equipment at release", () =>
        {
            var item = Copy("GR36"); var p = Scene(item); p.InventoryController.Inventory.Equipment.Slots.Add(new() { ContainedItem = new Weapon() }); var target = new Player();
            var a = Throw(p, item); p.InventoryController.Inventory.Equipment.Slots.Add(new() { ContainedItem = new Weapon { WeapClass = "assaultRifle" } });
            near(Hit(target, p, a.WeaponSource), 32, "release snapshot survives later gear change");
            var b = Throw(p, item); near(Hit(target, p, b.WeaponSource), 20, "another equipped firearm disables signature");
        });
        run("Juggernaut blocks only the thrower's own grenade blast and fragments", () =>
        {
            var item = Copy("GR21"); var p = Scene(item); var g = Throw(p, item); var target = new Player();
            near(Hit(p, p, g.WeaponSource), 0, "own blast immune"); near(Hit(p, p, g.WeaponSource, false), 0, "own fragments immune");
            near(Hit(target, p, g.WeaponSource), 20, "enemy still takes damage"); near(Hit(p, target, new ThrowWeap()), 20, "other grenades retain damage");
        });
        run("Iron Curtain protects against native Bullet-tagged grenade fragments and blasts", () =>
        {
            var item = Copy("GR09"); var p = Scene(item); Throw(p, item); var enemy = new Player(); var normalGrenade = new ThrowWeap();
            near(Hit(p, enemy, normalGrenade), 2, "90% less blast damage"); near(Hit(p, enemy, normalGrenade, false), 2, "90% less ballistic fragment damage");
            near(Hit(p, enemy, new Weapon(), type: EDamageType.Bullet), 20, "firearm bullets unmodified");
            Time.time += 8; near(Hit(p, enemy, normalGrenade, false), 20, "fragment protection expires at 8 seconds");
        });
        run("Red Notice requires a real fragment flesh hit and excludes blasts and stopped rounds", () =>
        {
            var item = Copy("GR07"); var p = Scene(item); var g = Throw(p, item); var target = new Player();
            Hit(target, p, g.WeaponSource, false, EBodyPart.LeftArm); check(target.ActiveHealthController.Heavy.Contains(EBodyPart.LeftArm), "fragment flesh hit bleeds");
            Hit(target, p, g.WeaponSource, true, EBodyPart.RightArm); Hit(target, p, g.WeaponSource, false, EBodyPart.LeftLeg, true); Hit(target, p, g.WeaponSource, false, EBodyPart.RightLeg, amount: 0);
            check(target.ActiveHealthController.Heavy.Count == 1, "blast, armor stop and no damage do not cause signature bleed");
        });
        run("Fragment damage bonus scales actual shots and leaves unrelated projectiles unchanged", () =>
        {
            var item = Copy("GR07"); var p = Scene(item); RarityResolver.Resolve(item)!.GrenadeFragmentDamageMultiplier = 1.25f; var g = Throw(p, item);
            var shot = new Shot(); LegendaryGrenadeEffects.Fragment(shot, g.WeaponSource); near(shot.Damage, 125, "fragment shot bonus");
            var normal = new Shot(); LegendaryGrenadeEffects.Fragment(normal, new Item()); near(normal.Damage, 100, "ordinary shot");
        });
        run("Blast Em replaces only the native blast falloff curve", () =>
        {
            var inverse = typeof(Mathf).GetMethod(nameof(Mathf.InverseLerp))!;
            using var stream = typeof(GrenadeFeatureTests).Assembly.GetManifestResourceStream("Grenades.BlastNativeIL.json")!;
            var native = System.Text.Json.JsonDocument.Parse(stream).RootElement.EnumerateArray().ToArray();
            var ops = typeof(OpCodes).GetFields().Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Name!);
            var input = native.Select(n => new CodeInstruction(ops[n.GetProperty("opcode").GetString()!],
                n.GetProperty("name").GetString() == "InverseLerp" && n.GetProperty("owner").GetString() == "UnityEngine.Mathf" ? inverse :
                n.GetProperty("name").GetString() == "get_GetStrength" ? typeof(IExplosiveItem).GetProperty("GetStrength")!.GetMethod : null)).ToArray();
            var output = ((IEnumerable<CodeInstruction>)typeof(LegendaryGrenadeFalloffPatch).GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [input])!).ToArray();
            check(output.Count(i => i.operand is MethodInfo m && m.Name == "Falloff") == 1 && output.Count(i => Equals(i.operand, inverse)) == 3, "real 4.0.13 IL windows: 1 blast curve changed; 3 native curves preserved");
            var item = Copy("GR11"); var p = Scene(item); var g = Throw(p, item); LegendaryGrenadeEffects.Try(g.WeaponSource, out var payload);
            LegendaryGrenadeEffects.CurrentExplosion = payload; near(LegendaryGrenadeFalloffPatch.Falloff(8, 4, 7.99f), 1, "full strength at far edge");
            LegendaryGrenadeEffects.CurrentExplosion = null; near(LegendaryGrenadeFalloffPatch.Falloff(8, 4, 7), .25f, "ordinary falloff unchanged");
        });
        run("Throw-triggered buffs start after release, refresh without stacking and expire exactly", () =>
        {
            foreach (var (key, seconds, kind) in new[] { ("GR03", 5f, "reload"), ("GR09", 8f, "protection"), ("GR18", 10f, "packing"), ("GR25", 6f, "hip"), ("GR49", 5f, "movement") })
            {
                var item = Copy(key); var p = Scene(item); Throw(p, item);
                float Value() => kind switch { "reload" => GrenadeBuffs.Reload(p), "protection" => GrenadeBuffs.Incoming(p), "packing" => GrenadeBuffs.Packing(p), "hip" => GrenadeBuffs.Hip(p), _ => GrenadeBuffs.Movement(p) };
                var active = Value(); check(active != 1, "buff armed " + key); Throw(p, item); near(Value(), active, "no stacking");
                Time.time = 100 + seconds - .001f; near(Value(), active, "before deadline"); Time.time = 100 + seconds; near(Value(), 1, "exact expiry");
            }
        });
        run("Rapid Response is consumed by 1 firearm draw rather than queries", () =>
        {
            var item = Copy("GR20"); var p = Scene(item); Throw(p, item); near(GrenadeBuffs.Draw(p), 2, "armed"); near(GrenadeBuffs.Draw(p), 2, "queries free");
            var hands = new Player.FirearmController { Owner = p }; object?[] args = [hands, 1f]; PatchHooks.Prefix("LegendaryGrenadeFirearmDrawPatch", args);
            near((float)args[1]!, 2, "real spawn doubles draw speed"); near(GrenadeBuffs.Draw(p), 1, "consumed once");
        });
        run("Fast Forward headset speeds ordinary and reusable grenade draw/throw without spending charges", () =>
        {
            foreach(var reusable in new[]{false,true})
            {
                var item=reusable?Copy("GR26"):new ThrowWeap();var p=Scene(item);
                var hands=new Player.BaseGrenadeHandsController{Owner=p,Item=item};p.HandsController=hands;
                var roll=RarityResolver.Resolve(item);var draw=roll?.GrenadeDrawSpeedMultiplier??1f;var throwing=roll?.GrenadeThrowSpeedMultiplier??1f;
                try
                {
                    EarpieceRuntime.Wearer=p;EarpieceRuntime.WornKey="EP07-2";
                    object?[] args=[hands,1.2f];PatchHooks.Prefix("LegendaryGrenadeDrawPatch",args);near((float)args[1]!,1.2f*draw*1.6f,"draw combines 60% signature with per-copy roll");
                    typeof(LegendaryGrenadeThrowAnimationPatch).GetMethod("Postfix",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,[hands]);near(p.HandsAnimator.Speed,throwing*1.6f,"throw animation combines same signature");
                    if(reusable)check(ReusableGrenadeRuntime.Remaining(item)==Designs["GR26"].GrenadeChargesPerRaid,"animation does not consume a charge");
                    EarpieceRuntime.Wearer=null;EarpieceRuntime.WornKey="";
                    args=[hands,1.2f];PatchHooks.Prefix("LegendaryGrenadeDrawPatch",args);near((float)args[1]!,1.2f*draw,"unequipped draw retains ordinary/per-copy value");
                }
                finally{EarpieceRuntime.Wearer=null;EarpieceRuntime.WornKey="";}
            }
        });
        run("Sun Staring Champion removes self flash and concussion while retaining enemy effects", () =>
        {
            var item = Copy("GR47"); var p = Scene(item); var enemy = new Player(); Singleton<GameWorld>.Instance.Actors.Add(enemy); var g = Throw(p, item);
            Flash(g, (p, 5, true), (enemy, 5, true)); near(p.ActiveHealthController.Flash, 0, "no own flash"); near(p.ActiveHealthController.Stun, 0, "no own stun"); check(enemy.ActiveHealthController.Flash > 0, "enemy flashed");
            LegendaryGrenadeEffects.Try(g.WeaponSource, out var payload); LegendaryGrenadeEffects.CurrentExplosion = payload; p.ActiveHealthController.DoContusion(10, 1);
            near(p.ActiveHealthController.Contusion, 0, "no own concussion"); LegendaryGrenadeEffects.CurrentExplosion = null;
        });
        run("Neuralyzer expands visual flash without expanding audio radius or bypassing cover", () =>
        {
            var item = Copy("GR51"); var p = Scene(item); RarityResolver.Resolve(item)!.GrenadeFlashRadiusMultiplier = 2; var enemy = new Player { IsAI = true }; Singleton<GameWorld>.Instance.Actors.Add(enemy); var g = Throw(p, item);
            Flash(g, (enemy, 30, true)); check(enemy.ActiveHealthController.Flash > 0 && enemy.AIData.BotOwner.FlashGrenade.Visual > 0, "visual flash beyond original 20 m range");
            near(enemy.AIData.BotOwner.FlashGrenade.Sound, 0, "sound remains inside original range");
            enemy.ActiveHealthController.Flash = 0; Physics.Blocked = true; Flash(g, (enemy, 5, true)); near(enemy.ActiveHealthController.Flash, 0, "normal cover checks");
            Physics.Blocked = false; Flash(g, (enemy, 5, false)); near(enemy.ActiveHealthController.Flash, 0, "normal facing check");
        });
        run("Daylight Robbery marks only successfully flashed targets for the thrower's bullets", () =>
        {
            var item = Copy("GR50"); var p = Scene(item); var target = new Player(); var other = new Player(); Singleton<GameWorld>.Instance.Actors.AddRange([target, other]); var g = Throw(p, item);
            Flash(g, (target, 5, true), (other, 5, false)); var gun = new Weapon();
            near(Hit(target, p, gun, type: EDamageType.Bullet), 26, "marked target +30%"); near(Hit(other, p, gun, type: EDamageType.Bullet), 20, "unflashed target normal");
            near(Hit(target, other, gun, type: EDamageType.Bullet), 20, "other shooter's bullets normal");
            near(Hit(target, p, new ThrowWeap(), false), 20, "grenade fragments do not count as firearm shots"); Time.time += 8; near(Hit(target, p, gun, type: EDamageType.Bullet), 20, "8-second expiry");
        });
        run("Flash Push grants 40% less recoil for 8 seconds after a successful flash", () =>
        {
            var item = Copy("GR54"); var p = Scene(item); var enemy = new Player(); Singleton<GameWorld>.Instance.Actors.Add(enemy); var g = Throw(p, item);
            near(GrenadeBuffs.Recoil(p), 1, "throw alone insufficient"); Flash(g, (enemy, 5, true)); near(GrenadeBuffs.Recoil(p), .6f, "flash activates recoil buff");
            Flash(g, (enemy, 5, true)); near(GrenadeBuffs.Recoil(p), .6f, "does not stack"); Time.time += 8; near(GrenadeBuffs.Recoil(p), 1, "expires");
        });
        run("Big Bang flashes on each detonation, not just the last charge", () =>
        {
            var item = Copy("GR35"); var p = Scene(item); var enemy = new Player(); Singleton<GameWorld>.Instance.Actors.Add(enemy);
            for (var i = 0; i < 3; i++) { enemy.ActiveHealthController.Flash = 0; var g = Throw(p, item); Flash(g, (enemy, 9, true)); check(enemy.ActiveHealthController.Flash > 0, "flash on throw " + (i + 1)); }
        });
        run("Smoke grenades retain native emission bindings and per-projectile duration bonuses", () =>
        {
            var item = Copy("GR62"); var p = Scene(item); RarityResolver.Resolve(item)!.GrenadeSmokeDurationMultiplier = 1.4f; var smoke = (SmokeGrenade)Throw(p, item);
            check(smoke.EmissionEnd != null && smoke.VelocityBelowThreshold != null, "emission and stall callbacks bound despite discard bypass");
            near(smoke.EmitTime, 126, "M18's 90 s multiplied by 1.4"); near(ItemFactory.Template(Designs["GR62"].SourceTemplateId).EmitTime, 90, "ordinary duration untouched");
        });
        run("Fog of War doubles every cloud dimension and composes its random size roll", () =>
        {
            var item = Copy("GR56"); var p = Scene(item); RarityResolver.Resolve(item)!.GrenadeSmokeSizeMultiplier = 1.2f; var smoke = (SmokeGrenade)Throw(p, item);
            var area = smoke._smokeGrenadeSettings._emissionArea; LegendaryGrenadeEffects.SmokeScale(smoke);
            near(area.transform.localScale.x, 2.4f, "horizontal x"); near(area.transform.localScale.y, 2.4f, "height"); near(area.transform.localScale.z, 2.4f, "horizontal z");
            object?[] radius = [smoke, 10f]; typeof(LegendaryGrenadeSmokeRadiusPatch).GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, radius); near((float)radius[1]!, 24, "AI smoke radius follows the expanded footprint");
        });
        run("Pop Smoke reaches its peak immediately and restores pooled emission settings", () =>
        {
            var item = Copy("GR59"); var p = Scene(item); var smoke = (SmokeGrenade)Throw(p, item);smoke.InvokeBlowUpEvent();
            var normalTime = .03f; LegendaryGrenadeEffects.SmokeArea(smoke, ref normalTime); near(normalTime, .3f, "immediate peak sample");
            LegendaryGrenadeEffects.Try(smoke.WeaponSource,out var payload);var emission=payload.Emission!;near(emission._defaultStartFillDelay,0,"no fill delay");near(emission.Prewarm,25,"native full-cloud particle prewarm");
            LegendaryGrenadeEffects.RestoreEmission(emission); near(emission._defaultStartFillDelay, 25, "pool defaults restored"); near(emission.transform.localScale.y, 1, "scale restored for ordinary smoke");
        });
        run("Smoke growth modifiers bind before the native in-hand fill coroutine starts", () =>
        {
            var item = Copy("GR59"); var p = Scene(item); RarityResolver.Resolve(item)!.GrenadeSmokeBuildTimeMultiplier = .2f;
            var hands = new Player.BaseGrenadeHandsController { Owner = p, Item = item }; p.HandsController = hands;
            var emission = new GrenadeEmission(); emission.AttachTo(hands._grenadeTransform, Vector3.zero); near(emission._defaultStartFillDelay, 5, "modified before StartEmission can capture its delay");
            LegendaryGrenadeEffects.RestoreEmission(emission); near(emission._defaultStartFillDelay, 25, "ordinary pool delay restored");
        });
        run("Smoke-area movement, recoil, packing, medicine and recovery are owner-only within 8 m", () =>
        {
            foreach (var key in new[] { "GR57", "GR58", "GR62", "GR63", "GR64" })
            {
                var item = Copy(key); var p = Scene(item); p.Crouched = true; var other = new Player { Crouched = true }; var smoke = (SmokeGrenade)Throw(p, item); smoke.InvokeBlowUpEvent();
                float Value(Player actor) => key switch { "GR57" => GrenadeBuffs.Movement(actor), "GR58" => GrenadeBuffs.Recoil(actor), "GR62" => GrenadeBuffs.Packing(actor), "GR63" => GrenadeBuffs.Medicine(actor), _ => GrenadeBuffs.Recovery(actor, "armRecovery") };
                check(Value(p) != 1, "owner affected " + key); near(Value(other), 1, "others not affected"); p.Position = new(8.01f, 0, 0); near(Value(p), 1, "outside radius");
                p.Position = new(8, 0, 0); check(Value(p) != 1, "8 m boundary included"); Time.time += smoke.LifeTime; near(Value(p), 1, "smoke expiry ends effect");
            }
        });
        run("Still Waters requires crouching and overlapping matching smokes do not stack", () =>
        {
            var a = Copy("GR58"); var b = Copy("GR58"); var p = Scene(a, b); Throw(p, a).InvokeBlowUpEvent(); Throw(p, b).InvokeBlowUpEvent();
            near(GrenadeBuffs.Recoil(p), 1, "standing no recoil bonus"); p.Crouched = true; near(GrenadeBuffs.Recoil(p), .4f, "60% reduction once despite 2 clouds");
        });
        run("Trailblazer keeps its native fuse minimum and waits for first landing before its cloud", () =>
        {
            var item = Copy("GR65"); var p = Scene(item); var g = Throw(p, item); g.InvokeBlowUpEvent(); check(Grenade.Blasts.Count == 0, "fuse elapsed in flight does not create mid-air area");
            check(LegendaryGrenadeEffects.Try(g.WeaponSource, out var payload) && payload.Emission == null, "parent does not emit in mid-air");
            LegendaryGrenadeEffects.Collision(g, new Collision()); check(Grenade.Blasts.Count == 1, "first landing starts normal smoke area");
            Time.time += 2; LegendaryGrenadeEffects.Collision(g, new Collision()); g.InvokeBlowUpEvent();
            check(Grenade.Blasts.Count == 1 && payload.SmokeStarted == 100, "later bounces cannot restart emission or its area lifetime");
        });
        run("All 37 grenades share the configured spawn budget and retain source filters", () =>
        {
            var variants = S.LegendaryCatalog.Load().Variants.Where(v => v.GrenadeChargesPerRaid > 0).ToArray();
            var originals = variants.Select(v => v.SourceTemplateId).Distinct().Select((id, n) => (id, weight: (double)(n + 1))).ToDictionary(v => v.id, v => v.weight);
            var cfg = new S.RarityConfiguration(); var policy = new S.GrenadeSpawnPolicy(variants); var pool = policy.Pool(originals, cfg, "standalone", "pmcusec", true);
            check(variants.All(v => pool.ContainsKey(v.TemplateId)), "every approved signature eligible");
            near((float)(variants.Sum(v => pool[v.TemplateId]) / pool.Values.Sum()), .01f, "1% combined grenade share");
            originals.Remove(Designs["GR06"].SourceTemplateId); var filtered = policy.Pool(originals, cfg, "standalone", "pmcusec", true);
            check(variants.Where(v => v.SourceTemplateId == Designs["GR06"].SourceTemplateId).All(v => !filtered.ContainsKey(v.TemplateId)), "filtered F-1 source cannot spawn its 3 legendary variants");
        });
        run("Green Room accelerates medical animation during the native controller transition", () =>
        {
            var item = Copy("GR63"); var p = Scene(item); Throw(p, item).InvokeBlowUpEvent();
            BackpackNative.MedicalFactor = 1;
            object?[] values = [1.2f]; PatchHooks.Prefix("BackpackUseAnimationPatch", p.HandsAnimator, values);
            near((float)values[0]!, 1.8f, "50% bonus before HandsController assignment");
            p.HandsController = new Player.MedsController { Item = new Item() }; BackpackNative.MedicalFactor = .5f;
            values = [1.2f]; PatchHooks.Prefix("BackpackUseAnimationPatch", p.HandsAnimator, values); near((float)values[0]!, 3.6f, "composes existing backpack use factor");
            BackpackNative.MedicalFactor = 1;
        });
        run("Recharge settings are configurable, sanitized and frozen for each raid", () =>
        {
            LegendaryGrenadeSettings.Bind(new BepInEx.Configuration.ConfigFile()); LegendaryGrenadeSettings.Reset();
            var field = typeof(LegendaryGrenadeSettings).GetField("Recharge", BindingFlags.NonPublic | BindingFlags.Static)!;
            var setting = (BepInEx.Configuration.ConfigEntry<float>)field.GetValue(null)!;
            setting.Value = 60; near(LegendaryGrenadeSettings.Interval(Designs["GR26"]), 60, "configured interval");
            setting.Value = 30; near(LegendaryGrenadeSettings.Interval(Designs["GR26"]), 60, "current raid unchanged");
            LegendaryGrenadeSettings.Reset(); near(LegendaryGrenadeSettings.Interval(Designs["GR26"]), 30, "next raid takes setting");
            setting.Value = float.NaN; LegendaryGrenadeSettings.Reset(); near(LegendaryGrenadeSettings.Interval(Designs["GR26"]), 180, "invalid values use default");
            setting.Value = 0; LegendaryGrenadeSettings.Reset(); near(LegendaryGrenadeSettings.Interval(Designs["GR26"]), 1, "minimum interval");
            setting.Value = 180; LegendaryGrenadeSettings.Reset();
        });
        run("Immediate smoke preserves its full-size curve sample for its entire 15-second lifetime", () =>
        {
            var owner = Scene(); SmokeLifecycleFixture.Reset(); var world=Singleton<GameWorld>.Instance;
            SmokeLifecycleFixture.DeploySmokeAt(owner,new(2,0,0),15,instant:true);
            check(SmokeLifecycleFixture.Count==1,"native smoke deployment succeeded"); var smoke=SmokeLifecycleFixture.Last;
            var peak=smoke._smokeGrenadeSettings._sizeOverTime.keys.OrderByDescending(k=>k.value).First().time;
            near(smoke.AreaTimes.Last(),peak,"starts full size");
            for(var n=1;n<15;n++){Time.time=100+n;SmokeLifecycleFixture.Maintain(world);near(smoke.AreaTimes.Last(),peak,"stays full size after maintenance");}
            Time.time=115;SmokeLifecycleFixture.Maintain(world);check(SmokeLifecycleFixture.Count==0,"expires at 15 seconds");
            SmokeLifecycleFixture.Reset();
        });
        run("Side Business still deploys after its thrower leaves the active-player list", () =>
        {
            var item=Copy("GR12");var owner=Scene(item);var grenade=Throw(owner,item);
            Singleton<GameWorld>.Instance.MainPlayer=new Player();check(GrenadeBuffs.Find(owner.ProfileId)==null,"owner is no longer registered");
            grenade.InvokeBlowUpEvent();check(AdvancedLegendaryActions.Smokes.Count==1,"signature depends on the grenade payload rather than finding a live owner");
        });
        run("Reset magazine packing loses its bonus when its 10-second timer expires mid-operation", () =>
        {
            var item=Copy("GR18");var p=Scene(item);Throw(p,item);
            var process=BackpackMagazineSpeedPatch.Load(p.InventoryController,new(),new(),30,false,4);
            near(RoundDelay(process),4/1.75f,"boosted round delay at start");Time.time+=10;
            near(RoundDelay(process),4,"next round uses normal delay after expiry");
        });
        run("FOB magazine packing rechecks smoke range for each round in both directions", () =>
        {
            var item=Copy("GR62");var p=Scene(item);Throw(p,item).InvokeBlowUpEvent();p.Position=new(9,0,0);
            var process=BackpackMagazineSpeedPatch.Unload(p.InventoryController,new(),4,false);near(RoundDelay(process),4,"outside at start");
            p.Position=new();near(RoundDelay(process),2,"entering smoke speeds the next round");p.Position=new(9,0,0);near(RoundDelay(process),4,"leaving ends the bonus");
        });
        run("Live packing changes preserve native elite progress and restore stored fields", () =>
        {
            var item=Copy("GR62");var p=Scene(item);Throw(p,item).InvokeBlowUpEvent();
            var process=BackpackMagazineSpeedPatch.Load(p.InventoryController,new(),new(),30,true,4);process._cooldown=1.6f;
            p.Position=new(9,0,0);near(RoundDelay(process),3.2f,"existing 20% elite speed progression survives the grenade bonus ending");
            near(process._cooldown,1.6f,"temporary round adjustment restores native progress field");
        });
        run("Door Breaching opens only nearby closed, unlocked, operatable swing doors", () =>
        {
            var item = Copy("GR01"); var p = Scene(item); RarityResolver.Resolve(item)!.GrenadeDoorBreach = true;
            var nearDoor = new EFT.Interactive.Door(); var locked = new EFT.Interactive.Door { DoorState = EFT.Interactive.EDoorState.Locked }; var quest = new EFT.Interactive.Door { Operatable = false }; var far = new EFT.Interactive.Door(); far.transform.position = new(3.01f, 0, 0);
            Throw(p, item).InvokeBlowUpEvent(); check(nearDoor.IsBroken && !locked.IsBroken && !quest.IsBroken && !far.IsBroken, "respects door state, distance and supported controls");
        });
        Revision(run, check, near);
        StatPresentation(run, check, near);
        FlashPrototypes(run, check, near);
        StatusAudit(run, check, near);
    }
}
