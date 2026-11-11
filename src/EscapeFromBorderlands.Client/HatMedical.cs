using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;

namespace EscapeFromBorderlands.Client;

internal static class HatMedical
{
    private sealed class Use
    {
        internal Headwear.State State = null!;
        internal Item Item = null!;
        internal EBodyPart Part;
        internal int Motion;
        internal float Hp, ChargeSpeed = 1;
        internal bool ExtraHeal;
    }
    private static readonly Dictionary<object, Use> Uses = new();
    private static readonly Dictionary<Player, Use> ByPlayer = new();
    internal static void Reset() { Uses.Clear(); ByPlayer.Clear(); }
    internal static bool Active(Headwear.State state) => ByPlayer.TryGetValue(state.Player, out var u) && u.State == state && u.ChargeSpeed > 1;
    private static bool Surgery(Item item) => Native.Component<HealthEffectsComponent>(item)?.DamageEffects?.Keys.Any(k => k.ToString() == "DestroyedPart") == true;
    private static float Charge(Headwear.State s, Item item) => s.Charges == 0 ? 1f : s.Key switch
    {
        "HW022-1" => 1.80f, "HW035-2" => 2f, "HW042-1" => 1.60f,
        "HW064-3" or "HW084-1" when s.InWindow => 2f, "HW085-3" when Surgery(item) => 2f, _ => 1f
    };
    internal static float Speed(Player? p, Item? item)
    {
        if (p is null || item is not MedsItemClass || Headwear.Get(p) is not { } s) return 1;
        return Headwear.Factor(s, HatStat.Medical) * (ByPlayer.TryGetValue(p, out var u) && u.State == s && u.Item == item ? u.ChargeSpeed : Charge(s, item));
    }
    internal static void Start(object effect)
    {
        if (Native.Get(effect, "MedItem") is not Item item || Headwear.Get(BackpackRuntime.Actor(Native.Get(effect, "HealthController"))) is not { } s) return;
        var part = Native.Get(effect, "BodyPart") is EBodyPart b ? b : EBodyPart.Chest;
        var u = new Use { State = s, Item = item, Part = part, Motion = s.Motion, Hp = s.Player.ActiveHealthController.GetBodyPartHealth(part, false).Current };
        if (item is MedsItemClass)
        {
            u.ChargeSpeed = Charge(s, item);
            if (u.ChargeSpeed > 1) s.Charges--;
            u.ExtraHeal = s.Key == "HW092-3" && s.Charges > 0 && Native.Component<MedKitComponent>(item)?.HpResourceRate > 0;
        }
        Uses[effect] = ByPlayer[s.Player] = u;
    }
    internal static void End(object effect)
    {
        if (!Uses.TryGetValue(effect, out var u)) return;
        Uses.Remove(effect);
        var s = u.State;
        if (ByPlayer.GetValueOrDefault(s.Player) == u) ByPlayer.Remove(s.Player);
        if (Native.Get(effect, "Bool_2") is true || Headwear.Get(s.Player) != s) return;
        if (u.Item is MedsItemClass)
        {
            if (s.Key == "HW016-1" && u.Motion == s.Motion && s.Still && s.Ready) s.Buff(12, 25);
            if (u.ExtraHeal && s.Charges > 0 && s.Player.ActiveHealthController.GetBodyPartHealth(u.Part, false).Current > u.Hp)
            { s.Charges--; Headwear.Heal(s, 30, exact: u.Part); }
        }
        if (Native.Component<FoodDrinkComponent>(u.Item) is not null)
            Headwear.Ate(s, u.Item.Template.ParentId == "5448e8d64bdc2dce718b4568");
    }
}
