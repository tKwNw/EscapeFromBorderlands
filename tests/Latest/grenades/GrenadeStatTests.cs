using System.Globalization;
using System.Reflection;
using Comfort.Common;
using EFT.InventoryLogic;
using EFT.UI;
using EscapeFromBorderlands.Client;

internal static partial class GrenadeFeatureTests
{
    private static ThrowWeap StatCopy(string key, string? id = null)
    {
        var item = Copy(key, id);
        item.Template = ItemFactory.Template(Designs[key].SourceTemplateId);
        return item;
    }
    private static ItemAttribute Stat(ThrowWeap item, Enum id) => item.Attributes.Single(a => a.Id.Equals(id));
    private static void ConstructorStats(ThrowWeap item) => typeof(LegendaryGrenadeAttributePatch)
        .GetMethod("Postfix", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [item]);

    private static void StatPresentation(Action<string, Action> run, Action<bool, string> check, Action<float, float, string> near)
    {
        run("All 37 grenade designs show the same adjusted stats as their actual projectile", () =>
        {
            Scene();
            foreach (var definition in Designs.Values)
            for (var i = 0; i < 20; i++)
            {
                var item = StatCopy(definition.RuntimeKey, "stat-" + definition.RuntimeKey + "-" + i);
                var baseDamage = item.GetStrength; var baseCount = item.FragmentsCount; var baseDelay = item.GetExplDelay;
                ConstructorStats(item);
                var projectile = LegendaryGrenadeEffects.Projectile(RarityResolver.Resolve(item)!);
                var explosive = LegendaryGrenadeEffects.Explosion(projectile, projectile);
                near(Stat(item, EItemAttributeId.MaximumThrowDamage).Base(), explosive.GetStrength, "displayed unconditional blast damage");
                near(Stat(item, EItemAttributeId.ExplosionDistance).Base(), explosive.MinExplosionDistance, "displayed inner radius");
                check(Stat(item, EItemAttributeId.ExplosionDistance).StringValue() == explosive.MinExplosionDistance.ToString("0.##", CultureInfo.InvariantCulture) + " - " + explosive.MaxExplosionDistance.ToString("0.##", CultureInfo.InvariantCulture) + " meters", "complete displayed radius range");
                near(Stat(item, EItemAttributeId.FragmentsCount).Base(), explosive.FragmentsCount, "displayed rounded count");
                near(Stat(item, EItemAttributeId.ExplosionDelay).Base(), projectile.GetExplDelay, "displayed native fuse including signature");
                check(item.GetStrength == baseDamage && item.FragmentsCount == baseCount && item.GetExplDelay == baseDelay, "inspection leaves inventory getters vanilla");
                if (definition.GrenadeCategory == "smoke") near(Stat(item, LegendaryGrenadeStatId.SmokeDuration).Base(), projectile.EmitTime, "displayed rounded smoke lifetime");
                if (explosive.Blindness.y > 0)
                {
                    near(Stat(item, LegendaryGrenadeStatId.FlashRadius).Base(), explosive.Blindness.y, "displayed visual flash radius");
                    near(Stat(item, LegendaryGrenadeStatId.FlashDuration).Base(), explosive.Blindness.z, "displayed native flash duration");
                }
            }
        });
        run("Encore copies keep separate stat values through session resets and inspection order", () =>
        {
            Scene(); var a = StatCopy("GR26", "stats-a"); ConstructorStats(a);
            var values = a.Attributes.Select(v => v.StringValue()).ToArray();
            ThrowWeap? b = null;
            for (var i = 0; i < 100; i++)
            {
                b = StatCopy("GR26", "stats-b-" + i); ConstructorStats(b);
                if (!b.Attributes.Select(v => v.StringValue()).SequenceEqual(values)) break;
            }
            check(b != null && !b.Attributes.Select(v => v.StringValue()).SequenceEqual(values), "physical copies show different rolls");
            var other = b!.Attributes.Select(v => v.StringValue()).ToArray();
            RarityResolver.ResetSession(); ConstructorStats(b); ConstructorStats(a);
            check(a.Attributes.Select(v => v.StringValue()).SequenceEqual(values), "first copy's original values remain stable");
            check(b.Attributes.Select(v => v.StringValue()).SequenceEqual(other), "second copy's original values remain stable");
        });
        run("Repeated inspection preserves shared templates, other attributes, native delegates and charges", () =>
        {
            var a = StatCopy("GR26"); var ordinary = new ThrowWeap { Template = a.Template, StringTemplateId = Designs["GR26"].SourceTemplateId };
            Scene(a, ordinary); var template = a.Template;
            var ordinaryDelegates = ordinary.Attributes.Select(v => (v.Base, v.StringValue)).ToArray();
            var nativeRows = a.Attributes.ToArray();
            var foreign = new ItemAttribute(DayOfWeek.Monday) { Name = "Another mod", Base = () => 42 };
            a.Attributes.Add(foreign); ConstructorStats(a); var count = a.Attributes.Count;
            var original = (template.ExplDelay, template.Strength, template.FragmentsCount, template.MinExplosionDistance, template.MaxExplosionDistance, template.EmitTime);
            for (var i = 0; i < 50; i++) { ConstructorStats(a); ConstructorStats(ordinary); }
            check(a.Attributes.Count == count && a.Attributes.Select(v => v.Id).Distinct().Count() == count, "no duplicate rows after reopen");
            check(nativeRows.All(a.Attributes.Contains) && a.Attributes.Contains(foreign) && foreign.Base() == 42, "native row identity and third-party attribute retained");
            check(ordinary.Attributes.Select(v => (v.Base, v.StringValue)).SequenceEqual(ordinaryDelegates), "ordinary grenade delegates untouched");
            check((template.ExplDelay, template.Strength, template.FragmentsCount, template.MinExplosionDistance, template.MaxExplosionDistance, template.EmitTime) == original, "shared template unchanged");
            check(ReusableGrenadeRuntime.Remaining(a) == 3 && Singleton<ItemFactory>.Instance.Created == 0, "inspection neither consumes charges nor creates items");
            near(Stat(ordinary, EItemAttributeId.MaximumThrowDamage).Base(), ordinary.GetStrength, "ordinary damage remains native");
        });
        run("Fragment damage reads ballistic ammunition and count uses native away-from-zero rounding", () =>
        {
            var item = StatCopy("GR02"); Scene(item); var roll = RarityResolver.Resolve(item)!;
            roll.GrenadeFragmentMultiplier = 1.5f; roll.GrenadeFragmentDamageMultiplier = 1.25f;
            Singleton<ItemFactory>.Instance.ItemTemplates[item.FragmentType] = new AmmoTemplate { Damage = 80 };
            ConstructorStats(item);
            near(Stat(item, EItemAttributeId.FragmentsCount).Base(), 113, "75 times 1.5 rounds to 113");
            near(Stat(item, LegendaryGrenadeStatId.FragmentDamage).Base(), 100, "80 ballistic damage times 1.25");
            check(Stat(item, LegendaryGrenadeStatId.FragmentDamage).StringValue() == "100", "actual numeric fragment damage displayed");
            check(Singleton<ItemFactory>.Instance.Created == 0, "no fragment items created just to inspect stats");
        });
        run("Holy Hand Grenade and signature fragment counts appear without modifying inventory fuse", () =>
        {
            Scene(); var big = StatCopy("GR15"); big.Template.ExplDelay = 5; ConstructorStats(big);
            near(Stat(big, EItemAttributeId.ExplosionDelay).Base(), 3, "Holy Hand Grenade displays a fixed 3-second fuse");
            near(big.GetExplDelay, 5, "held item's base fuse not edited");
            near(Stat(big, EItemAttributeId.ExplosionDistance).Base(), big.MinExplosionDistance * RarityResolver.Resolve(big)!.GrenadeRadiusMultiplier, "Big One signature and random radius combine");
            foreach (var (key, factor) in new[] { ("GR02", 1.75f), ("GR06", 2f) })
            {
                var item = StatCopy(key); ConstructorStats(item);
                check(RarityResolver.Resolve(item)!.GrenadeFragmentMultiplier >= factor, "signature fragment multiplier is part of roll");
                check(Stat(item, EItemAttributeId.FragmentsCount).Base() >= (int)Math.Round(item.FragmentsCount * factor, MidpointRounding.AwayFromZero), "signature count included in display");
            }
        });
        run("Flash rows match Big Bang fixed limits and Neuralyzer signature plus random rolls", () =>
        {
            Scene(); var bang = StatCopy("GR35"); var roll = RarityResolver.Resolve(bang)!;
            roll.GrenadeFlashRadiusMultiplier = 4; roll.GrenadeFlashDurationMultiplier = 3; ConstructorStats(bang);
            near(Stat(bang, LegendaryGrenadeStatId.FlashRadius).Base(), 25, "actual fixed Big Bang radius");
            near(Stat(bang, LegendaryGrenadeStatId.FlashDuration).Base(), 20, "actual fixed Big Bang duration");
            var neuralyzer = StatCopy("GR51"); ConstructorStats(neuralyzer); var p = RarityResolver.Resolve(neuralyzer)!;
            near(Stat(neuralyzer, LegendaryGrenadeStatId.FlashRadius).Base(), neuralyzer.Blindness.y * p.GrenadeFlashRadiusMultiplier, "signature and random flash multiplier");
            near(Stat(neuralyzer, LegendaryGrenadeStatId.FlashDuration).Base(), neuralyzer.Blindness.z * p.GrenadeFlashDurationMultiplier, "random duration");
        });
        run("Fog of War stats match actual visible cloud and rounded lifetime", () =>
        {
            var item = StatCopy("GR56"); var player = Scene(item); var roll = RarityResolver.Resolve(item)!;
            roll.GrenadeSmokeSizeMultiplier = 1.3f; roll.GrenadeSmokeDurationMultiplier = 1.25f;
            ConstructorStats(item); var grenade = Throw(player, item);
            LegendaryGrenadeEffects.Try(grenade.WeaponSource, out var payload);
            near(Stat(item, LegendaryGrenadeStatId.SmokeSize).Base(), payload.Emission!.transform.localScale.x, "display matches emitted width, height and depth multiplier");
            check(Stat(item, LegendaryGrenadeStatId.SmokeSize).StringValue() == "260%", "signature doubling and random bonus shown together");
            near(Stat(item, LegendaryGrenadeStatId.SmokeDuration).Base(), grenade.WeaponSource.EmitTime, "displayed lifetime matches projectile");
            item.Template.EmitTime = 1; roll.GrenadeSmokeDurationMultiplier = 2.5f; ConstructorStats(item);
            near(Stat(item, LegendaryGrenadeStatId.SmokeDuration).Base(), 2, "native smoke rounding remains to even");
        });
        run("Pop Smoke reports instant deployment and compatible copies show their actual growth bonus", () =>
        {
            Scene(); var pop = StatCopy("GR59"); ConstructorStats(pop);
            check(Stat(pop, LegendaryGrenadeStatId.SmokeDeployment).StringValue() == "Instant", "instant smoke activation shown");
            near(Stat(pop, LegendaryGrenadeStatId.SmokeDeployment).Base(), 0, "instant build-up has 0 delay in comparisons");
            var smoke = StatCopy("GR62"); var roll = RarityResolver.Resolve(smoke)!; roll.GrenadeSmokeBuildTimeMultiplier = 1 / 1.4f;
            ConstructorStats(smoke);
            check(Stat(smoke, LegendaryGrenadeStatId.SmokeDeployment).StringValue() == "71.43% of base", "faster growth shows reduced build-up time");
            check(Stat(smoke, LegendaryGrenadeStatId.SmokeDeployment).LessIsGood, "comparisons show shorter build-up as better");
            check(!pop.Attributes.Any(a => a.Id.Equals(LegendaryGrenadeStatId.FlashRadius)), "smoke does not invent a flash radius");
        });
        run("Draw, throw and range rows reflect the copy's own multipliers", () =>
        {
            Scene(); var item = StatCopy("GR40"); var roll = RarityResolver.Resolve(item)!; ConstructorStats(item);
            near(Stat(item, LegendaryGrenadeStatId.ThrowDistance).Base(), roll.GrenadeThrowDistanceMultiplier, "includes Shagbag range signature");
            near(Stat(item, LegendaryGrenadeStatId.DrawSpeed).Base(), roll.GrenadeDrawSpeedMultiplier, "draw speed");
            near(Stat(item, LegendaryGrenadeStatId.ThrowSpeed).Base(), roll.GrenadeThrowSpeedMultiplier, "release speed");
            check(Stat(item, LegendaryGrenadeStatId.ThrowDistance).StringValue().EndsWith('%'), "relative stat clearly labelled as percent");
        });
        run("Inspection and comparison bind existing copies and add late fragment data once", () =>
        {
            Scene(); var item = StatCopy("GR26"); Singleton<ItemFactory>.Instance.ItemTemplates.Clear();
            var panel = new ItemSpecificationPanel { _item = item }; PatchHooks.Prefix("LegendaryGrenadeInspectStatsPatch", panel);
            check(!item.Attributes.Any(a => a.Id.Equals(LegendaryGrenadeStatId.FragmentDamage)), "unknown fragment data not invented");
            Singleton<ItemFactory>.Instance.ItemTemplates[item.FragmentType] = new AmmoTemplate { Damage = 40 };
            PatchHooks.Prefix("LegendaryGrenadeInspectStatsPatch", panel);
            near(Stat(item, LegendaryGrenadeStatId.FragmentDamage).Base(), 40 * RarityResolver.Resolve(item)!.GrenadeFragmentDamageMultiplier, "late native ammo available on reopen");
            var other = StatCopy("GR26"); PatchHooks.Prefix("LegendaryGrenadeCompareStatsPatch", other);
            near(Stat(other, EItemAttributeId.MaximumThrowDamage).Base(), other.GetStrength * RarityResolver.Resolve(other)!.GrenadeDamageMultiplier, "comparison uses compared copy's roll");
            PatchHooks.Prefix("LegendaryGrenadeCompareStatsPatch", new object?[] { null });
            panel._item = new Item(); PatchHooks.Prefix("LegendaryGrenadeInspectStatsPatch", panel);
            check(item.Attributes.Count(a => a.Id.Equals(LegendaryGrenadeStatId.FragmentDamage)) == 1, "no duplicate fragment row");
        });
        run("Extra rows use supported native icons while other mods' enum ids stay untouched", () =>
        {
            foreach (var stat in Enum.GetValues<LegendaryGrenadeStatId>())
            {
                object?[] args = [stat]; PatchHooks.Prefix("LegendaryGrenadeStatIconPatch", args);
                check(args[0] is EItemAttributeId, "all custom rows map to supported native grenade icons");
            }
            Enum other = DayOfWeek.Monday; object?[] otherArgs = [other]; PatchHooks.Prefix("LegendaryGrenadeStatIconPatch", otherArgs);
            check(ReferenceEquals(otherArgs[0], other), "foreign custom enum untouched");
            object?[] nativeArgs = [EItemAttributeId.FragmentsCount]; PatchHooks.Prefix("LegendaryGrenadeStatIconPatch", nativeArgs);
            check(nativeArgs[0]!.Equals(EItemAttributeId.FragmentsCount), "native icon untouched");
        });
        run("Conditional target bonuses stay conditional rather than inflating base damage stats", () =>
        {
            Scene();
            foreach (var key in new[] { "GR01", "GR24", "GR27", "GR36" })
            {
                var item = StatCopy(key); ConstructorStats(item); var roll = RarityResolver.Resolve(item)!;
                near(Stat(item, EItemAttributeId.MaximumThrowDamage).Base(), item.GetStrength * roll.GrenadeDamageMultiplier, "display only unconditional damage for " + key);
                check(roll.BuildDescriptionBlock().Contains(roll.Definition.EffectText), "conditional signature still described");
            }
        });
    }
}
