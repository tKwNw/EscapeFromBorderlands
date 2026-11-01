using System;
using System.Collections.Generic;

namespace EscapeFromBorderlands.Client;

internal sealed class EarpieceBoons
{
    internal float Hearing = 1f, Ambient = 1f, SelfNoise = 1f, Contusion = 1f;
    internal float AimCost = 1f, Draw = 1f, LegRecovery = 1f, Aim = 1f;
}

// No Unity state here: timers and cartridge charges are tested against a
// controlled clock. Only cooldown deadlines survive removing an earpiece.
internal sealed class EarpieceEffects
{
    internal readonly string Key;
    private readonly Func<float> Clock;
    private readonly IDictionary<string, float> Cooldowns;
    private readonly Queue<float> RecentShots = new();
    private float StillSince, ProneSince = -1f, AdsSince = -1f, LastShot;
    private bool WasSprint;
    private float PreviousArms = -1f;
    internal float ActiveUntil, WalkDistance;
    internal int Charges;
    internal EarpieceEffects(string key, Func<float> clock, IDictionary<string, float> cooldowns)
    { Key = key; Clock = clock; Cooldowns = cooldowns; StillSince = LastShot = clock(); }
    internal float StillSeconds => Math.Max(0f, Clock() - StillSince);
    internal float ProneSeconds => ProneSince < 0f ? 0f : Math.Max(0f, Clock() - ProneSince);
    internal float AdsSeconds => AdsSince < 0f ? 0f : Math.Max(0f, Clock() - AdsSince);
    internal bool Active => Clock() < ActiveUntil;
    internal int ReadyShots => Key == "EP01-5" && Clock() - LastShot >= 10f ? 3 : Charges;
    internal bool Ready => !Cooldowns.TryGetValue(Key, out var until) || Clock() >= until;
    internal float Cooldown => Cooldowns.TryGetValue(Key, out var until) ? Math.Max(0f, until - Clock()) : 0f;
    private void Arm(float seconds, float cooldown = 0f)
    { ActiveUntil = Clock() + seconds; if (cooldown > 0f) Cooldowns[Key] = Clock() + cooldown; }

    // Returns fractions of maximum stamina to restore, independently of the
    // native regeneration calculation and its ordinary restrictions.
    internal (float Legs, float Arms) Tick(bool moving, bool sprint, bool prone, bool aiming, float walked, float arms)
    {
        var now = Clock();
        if (moving) StillSince = now;
        if (!prone) ProneSince = -1f; else if (ProneSince < 0f) ProneSince = now;
        if (!aiming) AdsSince = -1f; else if (AdsSince < 0f) AdsSince = now;
        if (Key == "EP04-1" && WasSprint && !sprint && Ready) Arm(5f, 10f);
        WasSprint = sprint;
        float legs = 0f, hands = 0f;
        if (Key == "EP02-3")
        {
            if (sprint) WalkDistance = 0f;
            else
            {
                WalkDistance += Math.Max(0f, walked);
                if (WalkDistance >= 20f) { WalkDistance %= 20f; legs = .2f; }
            }
        }
        if (Key == "EP03-2" && PreviousArms > 0f && arms <= 0f && Ready)
        { hands = .5f; Cooldowns[Key] = now + 45f; }
        PreviousArms = arms;
        return (legs, hands);
    }
    internal void Check(int bit)
    {
        if (!Ready) return;
        if (Key == "EP01-4" && bit == 2) Arm(6f, 15f);
        if (Key == "EP10-4" && bit == 1) { Charges = 2; Cooldowns[Key] = Clock() + 20f; }
    }
    internal void Damaged(float healthFraction)
    {
        if (!Ready) return;
        if (Key == "EP03-3") Arm(8f, 25f);
        if (Key == "EP04-5" && healthFraction <= .3f) Arm(6f, 45f);
    }
    internal bool Killed(bool firearm, float distance)
    {
        if (Key == "EP05-3" && distance <= 30f) Arm(20f);
        if (Key == "EP10-2" && firearm) Arm(5f);
        if (Key == "EP06-4" && firearm && Ready)
        { Cooldowns[Key] = Clock() + 10f; return true; }
        return false;
    }
    internal void Switched(bool fromMelee, bool toFirearm)
    { if (Key == "EP06-2" && fromMelee && toFirearm && Ready) Arm(4f, 20f); }
    internal void MalfunctionCleared()
    { if (Key == "EP06-5" && Ready) Arm(10f, 30f); }
    internal void Breached()
    { if (Key == "EP07-1" && Ready) Arm(10f, 20f); }
    internal void Ate()
    { if (Key == "EP07-5" && Ready) Arm(20f, 45f); }
    internal float Damage => Key switch
    {
        "EP01-5" when Charges > 0 || Clock() - LastShot >= 10f => 1.25f,
        "EP06-2" when Active => 1.3f,
        "EP07-1" when Active => 1.25f,
        "EP10-4" when Charges > 0 => 1.25f,
        _ => 1f
    };
    internal float FireRate => Active ? Key == "EP06-3" ? 1.2f : Key == "EP06-5" ? 1.4f : 1f : 1f;
    internal float Reload => Active ? Key == "EP03-3" ? 1.35f : Key == "EP10-2" ? 1.5f : 1f : 1f;
    internal float Aim => Active ? Key is "EP01-4" ? 1.4f : Key is "EP04-1" ? 1.5f : Key is "EP03-3" ? 1.25f : 1f : 1f;
    internal float Hearing => Key == "EP05-3" && Active ? 1.5f : 1f;
    // Called once for a successfully fired cartridge, before any of its pellets
    // or added projectiles are created. Damage is snapshotted for that cartridge.
    internal float Fire()
    {
        var now = Clock();
        if (Key == "EP01-5" && now - LastShot >= 10f) Charges = 3;
        var damage = Damage;
        if (Charges > 0) Charges--;
        LastShot = now;
        if (Key == "EP06-3")
        {
            while (RecentShots.Count > 0 && now - RecentShots.Peek() > 2f) RecentShots.Dequeue();
            RecentShots.Enqueue(now);
            if (RecentShots.Count >= 5 && Ready) { Arm(4f, 12f); RecentShots.Clear(); }
        }
        return damage;
    }
}
