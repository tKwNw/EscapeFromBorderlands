using System;
using System.Collections.Generic;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace EscapeFromBorderlands.Client;

internal static partial class Eyewear
{
    // Equipment/kill/movement transitions queue their own refresh. Shot-local
    // recoil is composed by the ordinary cartridge preparation path. No
    // eyewear effect requires the per-frame live firearm polling route.
    internal static bool ChangesFirearm(Player? p) => false;
    private static bool Relevant(HatStat stat) => stat is HatStat.Aim or HatStat.Recoil or HatStat.Sway or HatStat.Hip or HatStat.Search or
        HatStat.FireRate or HatStat.Ergo or HatStat.Reload or HatStat.Draw or HatStat.Medical or HatStat.Resist;
    internal static float Factor(Player? p, HatStat stat) => Relevant(stat) ? Factor(Get(p), stat) : 1;
    internal static float Factor(State? s, HatStat stat)
    {
        if (s is null || !Relevant(stat) || !s.Human && stat is HatStat.Sway or HatStat.Hip or HatStat.Search or HatStat.Medical or HatStat.Draw)
            return 1;
        var roll = s.Roll.Eyewear;
        var aim = stat is HatStat.Recoil or HatStat.Sway && s.Player.HandsController?.IsAiming == true;
        var value = stat switch
        {
            HatStat.Aim => 1 + roll.Aim,
            HatStat.Recoil when aim => 1 - roll.Recoil,
            HatStat.Sway when s.Human => (1 - roll.Sway) * (s.Moving && aim ? 1 - roll.MovingSway : 1),
            HatStat.Hip when s.Human && s.Still => 1 - roll.Hip,
            HatStat.Search when s.Human => 1 + roll.Search,
            _ => 1f
        };
        var signature = (s.Key, stat) switch
        {
            ("EW016", HatStat.FireRate or HatStat.Ergo or HatStat.Reload) => 1 + .02f * s.Counter,
            ("EW021", HatStat.Recoil) when s.Human && FreezeShot(s) => .5f,
            ("EW033", HatStat.Medical) when s.Human && s.Active => 1.6f,
            ("EW034", HatStat.Reload or HatStat.Draw) when s.Human && s.Active => 1.5f,
            _ => 1f
        };
        return value * signature;
    }
    internal static float AimStaminaFactor(Player? p) => ActorCapabilities.UsesStamina(p) && Get(p) is { } s &&
        p.HandsController?.IsAiming == true ? 1 - s.Roll.Eyewear.Arm : 1;
    internal static float LegStaminaFactor(Player? p) => ActorCapabilities.UsesStamina(p) && Get(p) is { Key: "EW030" } s && Time.time < s.Second ? 0 : 1;
    internal static IEnumerable<EffectStatusModel.Status> Status(Player? p)
    {
        if (!ActorCapabilities.IsHuman(p) || Get(p) is not { } s)
            yield break;
        var now = Time.time;
        RefreshCharge(s);
        var seconds = Math.Max(0, Math.Max(s.Until, s.Second) - now);
        var detail = "";
        var active = seconds > 0;
        if (s.Key == "EW017")
        {
            UpdateAmmo(s);
            if (s.StatusText.Length > 0)
            {
                detail = s.StatusText;
                active = true;
            }
        }
        else if (s.Key == "EW013" && p!.HandsController?.Item is Weapon { WeapClass: "shotgun" })
        {
            detail = "2x projectiles, -30% shotgun damage";
            active = true;
        }
        else if (s.Key == "EW016" && s.Counter > 0)
        {
            detail = s.StatusText;
            active = true;
        }
        else if (s.Charges > 0 && (s.Key != "EW020" || s.InWindow))
        {
            detail = "Ready";
            active = true;
        }
        else if (s.Key == "EW021" && s.AimSince > 0 && now - s.AimSince >= 1)
        {
            detail = "Ready";
            active = true;
        }
        else if (s.Key == "EW031" && now - s.LastShot >= 10)
        {
            detail = "Ready";
            active = true;
        }
        var recharge = s.RechargePeriod > 0 && s.Charges == 0 ? Math.Max(0, s.ChargeAt - now) : 0;
        var ready = Math.Max(recharge, Math.Max(0, s.Cooldown - now));
        if (active || ready > 0)
            yield return new EffectStatusModel.Status(s.StatusKey, s.Roll.Definition.ShortName, active, seconds, ready, detail);
    }
    private static void UpdateAmmo(State s)
    {
        if (s.Player.HandsController?.Item is not Weapon weapon)
        {
            s.StatusText = "";
            s.AmmoCount = -1;
            return;
        }
        var magazine = weapon.GetCurrentMagazine();
        AmmoItemClass? ammo = weapon is RevolverItemClass revolver && magazine is CylinderMagazineItemClass cylinder ?
            cylinder.GetFirstAmmo(!revolver.CylinderHammerClosed) : null;
        if (ammo is null)
            foreach (var chamber in weapon.Chambers)
                if (chamber.ContainedItem is AmmoItemClass { IsUsed: false } round)
                {
                    ammo = round;
                    break;
                }
        if (ammo is null && magazine?.Cartridges.Last is AmmoItemClass { IsUsed: false } next)
            ammo = next;
        var count = magazine?.Count ?? weapon.ChamberAmmoCount;
        var capacity = magazine?.MaxCount ?? weapon.Chambers.Length;
        var chamberCount = magazine is not null && magazine is not CylinderMagazineItemClass ? weapon.ChamberAmmoCount : 0;
        var id = ammo?.StringTemplateId ?? "";
        if (s.AmmoCount == count && s.AmmoCapacity == capacity && s.AmmoId == id && s.AmmoChamber == chamberCount)
            return;
        if (s.AmmoId != id || s.AmmoName.Length == 0)
            s.AmmoName = ammo is null ? "Empty" : (id + " ShortName").Localized();
        s.AmmoCount = count;
        s.AmmoCapacity = capacity;
        s.AmmoId = id;
        s.AmmoChamber = chamberCount;
        s.StatusText = s.AmmoName + " " + count + "/" + capacity + (chamberCount > 0 ? " +" + chamberCount : "");
    }
}
