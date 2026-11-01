using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

// Presentation reads the same eligibility helpers and ledger used by gameplay.
internal static class BackpackEffectStatus
{
    internal static void Add(List<EffectStatusModel.Status> rows, BackpackRuntime.State? s)
    {
        if (s is null || rows.Any(r => r.Key == s.Key)) return;
        var p = s.Player; var moving = p.InputDirection.sqrMagnitude > .0001f;
        bool? active = null; string timer = "";
        switch (s.Key)
        {
            case "BP02-1": active = p.MovementContext.IsSprintEnabled; break;
            case "BP05-2": active = p.ActiveHealthController.Hydration.Current <= 0f; break;
            case "BP06-2": active = s.Bag.GetAllItems().Where(i => Native.Component<FoodDrinkComponent>(i) is not null && i.Template.ParentId != "5448e8d64bdc2dce718b4568").Select(i => i.StringTemplateId).Distinct().Count() >= 2; break;
            case "BP07-3": active = BackpackRuntime.BlackLeg(p); break;
            case "BP08-3": active = new[] { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon }.All(slot => p.InventoryController.Inventory.Equipment.GetSlot(slot).ContainedItem is null); break;
            case "BP11-2": active = p.MovementContext.IsInPronePose && !moving; break;
            case "BP12-1": timer = "lootSprint"; break;
            case "BP13-1": active = !moving; break;
            case "BP14-3": active = s.ColdCharge; break;
            case "BP15-1": active = Native.Get(Native.Get(p.NightVisionObserver, "Component"), "IsOn") is true; break;
            case "BP16-1": active = s.JumpCharged || s.JumpSafe; break;
            case "BP17-1": timer = "fallSpeed"; break;
            case "BP22-3": active = BackpackRuntime.Woods(p); break;
            case "BP23-3":
                var remaining = s.Record.Until.Where(kv => kv.Key.StartsWith("bleed:", StringComparison.Ordinal)).Select(kv => kv.Value - Time.time).DefaultIfEmpty(0f).Max();
                rows.Add(new EffectStatusModel.Status(s.Key, s.Roll.Definition.ShortName, remaining > 0f, Math.Max(0f, remaining))); return;
            case "BP24-1": active = BackpackRuntime.Daytime(); break;
            case "BP24-2": active = s.Record.Categories.Count > 0; break;
            case "BP26-1": active = s.Record.Templates.Count >= 21; break;
            case "BP27-2": active = p.HandsController is Player.FirearmController f && f.Weapon.WeapClass == "sniperRifle"; break;
            case "BP28-3": active = p.MovementContext.IsInPronePose; break;
            case "BP30-1": active = BackpackRuntime.AimingLoadedPrimary(p); break;
            case "BP31-2": active = BackpackRuntime.Milestone(s); break;
            case "BP34-1":
                var lockout = EffectStatusModel.Remaining(s.Record.Until, "walkLockout", Time.time);
                rows.Add(new EffectStatusModel.Status(s.Key, s.Roll.Definition.ShortName, !p.MovementContext.IsSprintEnabled && lockout <= 0f, ready: lockout)); return;
            case "BP35-3": timer = "meleeReach"; break;
            case "BP37-2": timer = "milk"; break;
            case "BP38-1": active = p.SearchController?.SearchOperations?.Any(o => BackpackSearch.Corpse(o.Item)) == true; break;
            case "BP39-1": active = p.HandsController is Player.FirearmController pistol && pistol.Weapon.WeapClass == "pistol" && new[] { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon }.All(slot => p.InventoryController.Inventory.Equipment.GetSlot(slot).ContainedItem is null); break;
            case "BP40-2": active = s.Record.Active("traversal:sprint") || s.Record.Active("traversal:jump") || s.Record.Active("traversal:vault") ||
                BackpackRuntime.TraversalFactor(p, "sprint") < 1f || BackpackRuntime.TraversalFactor(p, "jump") < 1f || BackpackRuntime.TraversalFactor(p, "vault") < 1f; break;
            case "BP41-2": active = BackpackPresentation.FullCompartments(s.Bag); break;
            case "BP41-3": timer = "firstItem"; break;
            case "BP45-2": active = BackpackRuntime.MainGridFull(s.Bag); break;
        }
        if (timer != "")
        {
            var seconds = EffectStatusModel.Remaining(s.Record.Until, timer, Time.time);
            rows.Add(new EffectStatusModel.Status(s.Key, s.Roll.Definition.ShortName, seconds > 0f, seconds));
        }
        else if (active.HasValue) rows.Add(new EffectStatusModel.Status(s.Key, s.Roll.Definition.ShortName, active.Value));
    }
}
