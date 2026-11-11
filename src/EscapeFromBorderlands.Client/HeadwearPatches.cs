using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using CommonAssets.Scripts.Game;
using EFT;
using EFT.Animations;
using EFT.HealthSystem;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

[HarmonyPatch(typeof(Player), nameof(Player.OnItemAddedOrRemoved))]
internal static class HatEquipment
{
    private static void Postfix(Player __instance, ItemAddress __1)
    {
        if (__1 is GClass3391 { Slot: { } slot } && (slot.ParentItem is InventoryEquipment ||
            Headwear.Get(__instance) is { } s && Headwear.Attached(s, slot.ParentItem)))
            Headwear.EquipmentChanged(__instance);
    }
}
[HarmonyPatch(typeof(Player), nameof(Player.OnDead))]
internal static class HatDeath
{
    private static void Prefix(Player __instance)
    {
        try { Headwear.Dead(__instance); }
        catch (Exception e) { Plugin.LogSource.LogWarning($"EFB headwear kill processing: {e.Message}"); }
    }
}
[HarmonyPatch(typeof(Player), nameof(Player.ApplyShot))]
internal static class HatHit
{
    private static void Postfix(Player __instance, DamageInfoStruct damageInfo, ShotIdStruct shotId, ShotInfoClass __result)
    {
        if (__result is not null) Headwear.BulletImpact(__instance, damageInfo, shotId);
    }
    private static void Prefix(Player __instance, DamageInfoStruct damageInfo, EBodyPart bodyPartType)
    {
        _ = Headwear.Get(__instance);
        Headwear.Hit(__instance, damageInfo, bodyPartType);
    }
}
[HarmonyPatch(typeof(Player), nameof(Player.ApplyDamageInfo))]
internal static class HatIncoming
{
    private static bool Prefix(Player __instance, ref DamageInfoStruct damageInfo, EBodyPart bodyPartType) =>
        Headwear.Incoming(__instance, ref damageInfo, bodyPartType);
}
[HarmonyPatch(typeof(ActiveHealthController), nameof(ActiveHealthController.ApplyDamage))]
internal static class HatDamage
{
    private static void Prefix(ActiveHealthController __instance, ref float damage, ref DamageInfoStruct damageInfo,
        out (Headwear.State? State, float Before) __state)
    {
        __state = default;
        HeadwearDiagnostics.Damage(Headwear.Get(__instance.Player), damageInfo, "ApplyDamage entry", damage);
        if (Headwear.Immune(__instance.Player, damageInfo)) { damage = 0; damageInfo.Damage = 0; damageInfo.BleedBlock = true; return; }
        if (damage > 0 && __instance.IsAlive &&
            Headwear.Get(__instance.Player) is { Key: "HW005-1" or "HW019-1" or "HW038-2" or "HW067-3" or "HW073-3" or "HW085-1" } s)
            __state = (s, __instance.GetBodyPartHealth(EBodyPart.Common, false).Current);
    }
    private static void Postfix(ActiveHealthController __instance, EBodyPart bodyPart, DamageInfoStruct damageInfo, (Headwear.State? State, float Before) __state)
    {
        if (__state.State is { Key: "HW019-1" } traced)
            HeadwearDiagnostics.Damage(traced, damageInfo, "ApplyDamage exit (health lost)",
                __state.Before - __instance.GetBodyPartHealth(EBodyPart.Common, false).Current);
        if (__state.State is { } s && __instance.IsAlive && __instance.GetBodyPartHealth(EBodyPart.Common, false).Current < __state.Before)
            Headwear.Damaged(s, damageInfo, bodyPart);
    }
}
[HarmonyPatch(typeof(ArmorComponent), nameof(ArmorComponent.ApplyDamage))]
internal static class HatArmorImpact
{
    [ThreadStatic] private static (ArmorComponent? Armor, Headwear.State? State, float Factor) Impact;
    private static void Prefix(ArmorComponent __instance, DamageInfoStruct damageInfo, out (ArmorComponent?, Headwear.State?, float) __state)
    {
        __state = Impact;
        var s = Headwear.ForArmor(__instance);
        Impact = (__instance, s, Headwear.ImmuneArmor(__instance, damageInfo) ? 0 : s is not null ? Headwear.Wear(s, __instance, damageInfo) : 1);
    }
    private static void Postfix(ArmorComponent __instance, ref DamageInfoStruct damageInfo)
    {
        if (Impact.Armor == __instance && Impact.State is { } s && damageInfo.DamageType == EDamageType.Bullet &&
            damageInfo.Weapon is Weapon && (damageInfo.BlockedBy == __instance.Item.Id || damageInfo.DeflectedBy == __instance.Item.Id))
            Headwear.Stopped(s, __instance, ref damageInfo);
    }
    private static Exception? Finalizer((ArmorComponent?, Headwear.State?, float) __state, Exception? __exception) { Impact = __state; return __exception; }
    internal static float Factor(ArmorComponent armor) => Impact.Armor == armor ? Impact.Factor : 1;
}
[HarmonyPatch(typeof(ArmorComponent), nameof(ArmorComponent.ApplyDurabilityDamage))]
internal static class HatWear
{
    private static void Prefix(ArmorComponent __instance, ref float armorDamage) => armorDamage *= HatArmorImpact.Factor(__instance);
}
[HarmonyPatch(typeof(ArmorComponent), nameof(ArmorComponent.ApplyExplosionDurabilityDamage))]
internal static class HatExplosionWear
{
    private static void Prefix(ArmorComponent __instance, ref float armorDamage, DamageInfoStruct damageInfo)
    {
        if (Headwear.ImmuneArmor(__instance, damageInfo)) { armorDamage = 0; return; }
        if (Headwear.ForArmor(__instance) is not { } s) return;
        if (Headwear.Section(s, __instance.Item)) armorDamage *= Headwear.Factor(s, HatStat.Durability);
        if (s is { Key: "HW071-1", Visor: true }) armorDamage *= .25f;
    }
}
[HarmonyPatch(typeof(ArmorComponent), nameof(ArmorComponent.Deflects))]
internal static class HatRicochet
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getter = AccessTools.PropertyGetter(typeof(GInterface383), "RicochetVals");
        var count = 0;
        foreach (var code in instructions)
        {
            yield return code;
            if (!code.Calls(getter)) continue;
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(HatRicochet), nameof(Chance)));
            count++;
        }
        if (count != 1) throw new InvalidOperationException($"Expected one native ricochet vector, found {count}.");
    }
    private static Vector3 Chance(Vector3 original, ArmorComponent armor)
    {
        if (original.x <= 0 || Headwear.ForArmor(armor) is not { } s || !Headwear.Section(s, armor.Item)) return original;
        var factor = Headwear.Factor(s, HatStat.Ricochet);
        if (factor != 1) { original.x = Math.Min(.99f, original.x * factor); original.y = Math.Min(.99f, original.y * factor); }
        return original;
    }
}
[HarmonyPatch(typeof(ArmorComponent), "get_WeaponErgonomicPenalty")]
internal static class HatEquipmentErgo
{
    private static void Postfix(ArmorComponent __instance, ref float __result)
    {
        if (__result != 0 && Headwear.ForArmor(__instance) is { Key: "HW063-1", Marksman: true } s && Headwear.Section(s, __instance.Item))
            __result = 0;
    }
}
[HarmonyPatch(typeof(Weapon), "get_ErgonomicsTotal")]
internal static class HatFirearmErgo
{
    private static void Postfix(Weapon __instance, ref float __result)
    {
        var factor = Headwear.Factor(Headwear.Owner(__instance), HatStat.Ergo);
        if (factor != 1) __result = Math.Min(100, __result * factor);
    }
}
[HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.InitiateShot))]
internal static class HatCartridge
{
    [ThreadStatic] internal static (Weapon Gun, float Penetration)? Current;
    private static void Prefix(Player.FirearmController __instance, out (Weapon, float)? __state)
    {
        __state = Current;
        var factor = Headwear.Factor(WeaponCombatState.Owner(__instance), HatStat.Penetration);
        Current = factor == 1 ? null : (__instance.Item, factor);
    }
    private static void Postfix(Player.FirearmController __instance) => Headwear.Fired(WeaponCombatState.Owner(__instance));
    private static Exception? Finalizer((Weapon, float)? __state, Exception? __exception) { Current = __state; return __exception; }
}
[HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.GetMalfunctionState))]
internal static class HatMalfunction
{
    private static bool Prefix(Player.FirearmController __instance, ref Weapon.EMalfunctionState __result, ref Weapon.EMalfunctionSource __6)
    {
        if (__instance.Item.AmmoCaliber != "Caliber762x39" || !Headwear.Is(WeaponCombatState.Owner(__instance), "HW032-1")) return true;
        __result = default; __6 = default; return false;
    }
}
[HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.UpdateHipInaccuracy))]
internal static class HatHipSpread
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Player.FirearmController __instance)
    {
        var p = WeaponCombatState.Owner(__instance);
        var factor = Headwear.Factor(p, HatStat.Hip);
        if (p?.ProceduralWeaponAnimation is not { } animation || factor == 1) return;
        __instance.HipInaccuracy *= factor; animation.Breath.HipPenalty = __instance.HipInaccuracy;
    }
}
[HarmonyPatch(typeof(EffectsController), "method_7")]
internal static class HatDamageCamera
{
    private static readonly FieldInfo PlayerField = AccessTools.Field(typeof(EffectsController), "player_0");
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var apply = AccessTools.Method(typeof(ForceEffector), nameof(ForceEffector.AddForce), new[] { typeof(float), typeof(float), typeof(float) });
        var count = 0;
        foreach (var code in instructions)
        {
            if (code.Calls(apply))
            {
                var load = new CodeInstruction(OpCodes.Ldarg_0); code.MoveLabelsTo(load);
                yield return load;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(HatDamageCamera), nameof(Camera)));
                count++;
            }
            yield return code;
        }
        if (count != 1) throw new InvalidOperationException($"Expected one damage camera force, found {count}.");
    }
    private static float Camera(float native, EffectsController e) => native * Headwear.Factor(PlayerField.GetValue(e) as Player, HatStat.Camera);
}
[HarmonyPatch(typeof(Player), nameof(Player.Look))]
internal static class HatFreelook
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var count = 0;
        foreach (var code in instructions)
        {
            yield return code;
            if (code.opcode != OpCodes.Ldfld || code.operand is not FieldInfo field || field.DeclaringType != typeof(EFTHardSettings) ||
                field.Name is not ("MOUSE_LOOK_HORIZONTAL_LIMIT" or "MOUSE_LOOK_VERTICAL_LIMIT")) continue;
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(HatFreelook), nameof(Range)));
            count++;
        }
        if (count != 2) throw new InvalidOperationException($"Expected both native freelook limits, found {count}.");
    }
    private static Vector2 Range(Vector2 range, Player player) => range * Headwear.Factor(player, HatStat.Freelook);
}
[HarmonyPatch(typeof(MovementContext), "method_15")]
internal static class HatLean
{
    private static void Prefix(MovementContext __instance, ref float deltaTime) => deltaTime *= Headwear.Factor(BackpackRuntime.Actor(__instance), HatStat.Lean);
}
[HarmonyPatch(typeof(MovementContext), nameof(MovementContext.SmoothPoseLevel))]
internal static class HatStance
{
    private static void Prefix(MovementContext __instance, ref float deltaTime) => deltaTime *= Headwear.Factor(BackpackRuntime.Actor(__instance), HatStat.Stance);
}
[HarmonyPatch]
internal static class HatProneTransition
{
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.Method(typeof(Transit2ProneStateClass), nameof(Transit2ProneStateClass.Enter)),
        AccessTools.Method(typeof(Prone2StandStateClass), nameof(Prone2StandStateClass.Enter))
    };
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var getter = AccessTools.PropertyGetter(typeof(MovementContext), nameof(MovementContext.TransitionSpeed));
        var scaled = AccessTools.Method(typeof(HatProneTransition), nameof(ScaledSpeed));
        var count = 0;
        foreach (var code in instructions)
        {
            if (code.Calls(getter))
            {
                // Consume the same context and return the same type as the
                // native getter, preserving the setter's receiver below it.
                code.opcode = OpCodes.Call;
                code.operand = scaled;
                count++;
            }
            yield return code;
        }
        if (count != 1) throw new InvalidOperationException("Native prone transition speed unavailable.");
    }
    private static float ScaledSpeed(MovementContext m) => m.TransitionSpeed * Headwear.Factor(BackpackRuntime.Actor(m), HatStat.Stance);
}
[HarmonyPatch(typeof(Player.FirearmController), "method_16")]
internal static class HatSprintHands
{
    private static void Postfix(Player.FirearmController __instance, bool __0) =>
        Headwear.SprintHands(WeaponCombatState.Owner(__instance), __instance.FirearmsAnimator, __0);
}
[HarmonyPatch(typeof(Player.FirearmController), nameof(Player.FirearmController.Spawn))]
internal static class HatDrawStart
{
    private static void Prefix(Player.FirearmController __instance, ref float animationSpeed) =>
        animationSpeed = Headwear.BeginDraw(WeaponCombatState.Owner(__instance), __instance.Item, animationSpeed);
}
[HarmonyPatch(typeof(Player.FirearmController.GClass2055), nameof(Player.FirearmController.GClass2055.WeaponAppeared))]
internal static class HatDrawEnd
{
    private static void Postfix(Player.FirearmController.GClass2055 __instance) => Headwear.EndDraw(Headwear.Owner(__instance.Weapon_0));
}
[HarmonyPatch(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation.UpdateWeaponVariables))]
internal static class HatSway
{
    private static readonly FieldInfo Strength = AccessTools.Field(typeof(ProceduralWeaponAnimation), "_aimSwayStrength");
    private static void Postfix(ProceduralWeaponAnimation __instance)
    {
        var s = Headwear.Get(__instance);
        var factor = Headwear.Factor(s, HatStat.Sway);
        if (factor != 1) Strength.SetValue(__instance, (float)Strength.GetValue(__instance)! * factor);
        HatRecoilRecovery.Bind(__instance, s);
    }
}
[HarmonyPatch]
internal static class HatRecoilRecovery
{
    private static readonly ConditionalWeakTable<RecoilProcessBase, Headwear.State> Owners = new();
    internal static void Bind(ProceduralWeaponAnimation p, Headwear.State? s)
    {
        if (s?.Key != "HW091-3" || p.Shootingg?.CurrentRecoilEffect?.HandRotationRecoilEffect is not RecoilProcessBase recoil) return;
        Owners.Remove(recoil); Owners.Add(recoil, s);
    }
    private static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.Method(typeof(NewRotationRecoilProcess), nameof(NewRotationRecoilProcess.CalculateRecoil)),
        AccessTools.Method(typeof(OldRotationRecoilProcess), nameof(OldRotationRecoilProcess.CalculateRecoil))
    };
    private static void Prefix(RecoilProcessBase __instance, out float? __state)
    {
        __state = null;
        if (!Owners.TryGetValue(__instance, out var s) || !s.Active || Headwear.Get(s.Player) != s) return;
        __state = __instance.ReturnSpeed; __instance.ReturnSpeed *= Headwear.Factor(s, HatStat.Recovery);
    }
    private static Exception? Finalizer(RecoilProcessBase __instance, float? __state, Exception? __exception)
    { if (__state.HasValue) __instance.ReturnSpeed = __state.Value; return __exception; }
}
[HarmonyPatch(typeof(Player), nameof(Player.ApplyHitDebuff))]
internal static class HatSuppression
{
    private static bool Prefix(Player __instance, EDamageType damageType) =>
        damageType != EDamageType.Bullet || Headwear.Get(__instance) is not { Key: "HW088-1" } s || Time.time >= s.Second;
}
[HarmonyPatch(typeof(SkillClass), nameof(SkillClass.OnTrigger))]
internal static class HatSkillGain
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var award = AccessTools.Method(typeof(AbstractSkillClass), nameof(AbstractSkillClass.OnTrigger));
        var count = 0;
        foreach (var code in instructions)
        {
            if (code.Calls(award))
            {
                var load = new CodeInstruction(OpCodes.Ldarg_0); code.MoveLabelsTo(load);
                yield return load;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(HatSkillGain), nameof(Award)));
                count++;
            }
            yield return code;
        }
        if (count != 1) throw new InvalidOperationException("Native skill award unavailable.");
    }
    private static float Award(float value, SkillClass skill) => value * Headwear.Factor(Headwear.Get(skill.SkillManager), HatStat.Skill);
}
[HarmonyPatch(typeof(SmokeGrenade), nameof(SmokeGrenade.OnExplosion))]
internal static class HatSmokeStart { private static void Postfix(SmokeGrenade __instance) => Headwear.SmokeChanged(__instance, true); }
[HarmonyPatch(typeof(SmokeGrenade), "InvokeEmissionEndEvent")]
internal static class HatSmokeEnd { private static void Postfix(SmokeGrenade __instance) => Headwear.SmokeChanged(__instance, false); }
[HarmonyPatch(typeof(Player), nameof(Player.SetExfiltrationPoint))]
internal static class HatExfilEnter
{
    private static void Postfix(Player __instance, ExfiltrationPoint __0, bool __1)
    { if (__0 != null) Headwear.ExfilChanged(__instance, __0, __1); }
}
[HarmonyPatch(typeof(EndByExitTrigerScenario), "method_0")]
internal static class HatExfilStart
{
    private static void Postfix(EndByExitTrigerScenario __instance, ExfiltrationPoint trigger, Player player)
    {
        if (Native.Get(__instance, "list_0") is List<EndByExitTrigerScenario.Class984> entries &&
            entries.Any(e => e.Player == player && e.Trigger == trigger)) Headwear.ExfilChanged(player, trigger, true, true);
    }
}
[HarmonyPatch(typeof(EndByExitTrigerScenario), "method_1")]
internal static class HatExfilCancel { private static void Postfix(ExfiltrationPoint trigger, Player player) => Headwear.ExfilChanged(player, trigger, false); }
[HarmonyPatch(typeof(ExfiltrationPoint), nameof(ExfiltrationPoint.OnPlayerExit))]
internal static class HatExfilExit { private static void Postfix(ExfiltrationPoint __instance, Player __0) => Headwear.ExfilChanged(__0, __instance, false); }
[HarmonyPatch(typeof(GClass3515), "method_6")]
internal static class HatCorpseSearch
{
    private static void Postfix(GClass3515 __instance, ref Task __result)
    {
        var p = BackpackSearch.Owner(__instance);
        if (p is not null && Headwear.Is(p, "HW073-3") && BackpackSearch.CorpseRoot(__instance.Item) is { } root &&
            __instance.IplayerSearchController_0.ContainsUnknownItems(__instance.Item))
            __result = Wait(__result, __instance, p, root);
    }
    private static async Task Wait(Task search, GClass3515 op, Player p, InventoryEquipment root)
    {
        await search;
        if (!op.Boolean_0 && !op.IplayerSearchController_0.ContainsUnknownItems(op.Item) &&
            !root.GetAllItems().OfType<SearchableItemItemClass>().Any(i => op.IplayerSearchController_0.ContainsUnknownItems(i))) Headwear.Searched(p, root);
    }
}
[HarmonyPatch(typeof(Player.BaseGrenadeHandsController), "method_10")]
internal static class HatGrenadeRelease
{
    private static void Postfix(Player.BaseGrenadeHandsController __instance, Grenade __result)
    {
        if (__result != null && __instance.Item is ThrowWeapItemClass item) Headwear.Thrown(BackpackRuntime.Actor(__instance), __result, item);
    }
}
[HarmonyPatch(typeof(GClass2080), nameof(GClass2080.ApplyLightAndSoundHealthEffects))]
internal static class HatFlash
{
    private static void Prefix(ref List<IPlayerOwner> players)
    {
        for (var i = 0; i < players.Count; i++)
            if (players[i].iPlayer is Player p && Headwear.FlashImmune(p))
            {
                players = players.Where(x => x.iPlayer is not Player target || !Headwear.FlashImmune(target)).ToList();
                return;
            }
    }
}

internal static partial class Headwear
{
    internal static float BeginDraw(Player? p, Weapon weapon, float native)
    {
        if (Get(p) is not { } s || p is null) return native;
        var factor = Factor(s, HatStat.Draw);
        if (s.Key == "HW042-1" && s.DrawCharges > 0) factor *= 1.60f;
        if (s.Key == "HW008-2")
        {
            var gear = p.InventoryController.Inventory.Equipment;
            var one = gear.GetSlot(EquipmentSlot.FirstPrimaryWeapon)?.ContainedItem;
            var two = gear.GetSlot(EquipmentSlot.SecondPrimaryWeapon)?.ContainedItem;
            var old = s.Weapon == weapon ? s.PreviousWeapon : s.Weapon;
            if (one is Weapon && two is Weapon && (weapon == one && old == two || weapon == two && old == one)) factor *= 1.60f;
        }
        s.Drawing = factor != 1; s.DrawSpeed = native;
        return native * factor;
    }
    internal static void EndDraw(Player? p)
    {
        if (Get(p) is not { Drawing: true } s) return;
        if (s.Key == "HW042-1" && s.DrawCharges > 0) s.DrawCharges--;
        s.Drawing = false;
        if (p?.HandsController is Player.FirearmController f) f.FirearmsAnimator.SetAnimationSpeed(s.DrawSpeed);
    }
    internal static void SprintHands(Player? p, FirearmsAnimator animator, bool sprint)
    {
        if (Get(p) is not { } s) return;
        var previous = s.HandsSprint; s.HandsSprint = sprint;
        if (sprint) { RestoreSnap(s); return; }
        var factor = Factor(s, HatStat.SprintReady);
        if (!previous || factor == 1 || animator is null || Native.Get(animator, "Animator") is not { } raw) return;
        s.SnapSpeed = Native.Number(raw, "speed");
        if (s.SnapSpeed <= 0) return;
        s.Snap = animator; s.SnapUntil = Time.time + .30f / factor;
        s.SnapApplied = s.SnapSpeed * factor; animator.SetAnimationSpeed(s.SnapApplied);
    }
    private static void RestoreSnap(State s)
    {
        if (s.Snap is not { } animator) return;
        s.Snap = null;
        if (animator != null && Native.Get(animator, "Animator") is { } raw && Math.Abs(Native.Number(raw, "speed") - s.SnapApplied) < .001f)
            animator.SetAnimationSpeed(s.SnapSpeed);
    }
}
