using System;
using System.Linq;
using EscapeFromBorderlands.Client;
using EFT;
using Comfort.Common;
using UnityEngine;

internal static partial class GrenadeFeatureTests
{
    private static void StatusAudit(Action<string, Action> run, Action<bool, string> check, Action<float, float, string> near)
    {
        run("Daylight Robbery displays the real owned marked-target duration without spending charges", () =>
        {
            var item = Copy("GR50"); var p = Scene(item); var target = new Player();
            Singleton<GameWorld>.Instance.Actors.Add(target); var g = Throw(p, item);
            check(!GrenadeBuffs.Statuses(p).Any(s => s.Key == "grenade-buff:marked"), "no untriggered mark");
            Flash(g, (target, 5, true));
            for (var i = 0; i < 100; i++)
            {
                var row = GrenadeBuffs.Statuses(p).Single(s => s.Key == "grenade-buff:marked");
                check(row.Active && row.Name == Designs["GR50"].ShortName, "actual legendary mark shown");
                near(row.Seconds, 8f, "actual mark deadline");
            }
            check(ReusableGrenadeRuntime.Remaining(item) == 2, "display does not spend a charge");
            Time.time += 8f;
            check(!GrenadeBuffs.Statuses(p).Any(s => s.Key == "grenade-buff:marked"), "exact expiry removes mark");
        });
        run("Marked-target statuses stay owner-scoped and clear at raid reset", () =>
        {
            var item = Copy("GR50"); var p = Scene(item); var target = new Player(); var other = new Player();
            Singleton<GameWorld>.Instance.Actors.AddRange(new[] { target, other }); var g = Throw(p, item);
            Flash(g, (target, 5, true));
            check(!GrenadeBuffs.Statuses(other).Any(s => s.Key == "grenade-buff:marked"), "other actor has no inherited mark");
            target.HealthController.IsAlive = false;
            check(!GrenadeBuffs.Statuses(p).Any(s => s.Key == "grenade-buff:marked"), "dead target does not keep a row alive");
            GrenadeBuffs.Reset();
            check(!GrenadeBuffs.Statuses(p).Any(s => s.Key == "grenade-buff:marked"), "reset removes mark and presentation name");
        });
    }
}
