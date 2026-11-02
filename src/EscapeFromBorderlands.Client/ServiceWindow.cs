using System;
using System.Collections;
using EFT;
using EFT.InventoryLogic;

namespace EscapeFromBorderlands.Client;

internal static class ServiceWindow
{
    internal static bool Active(Player? player) => player?.IsInventoryOpened == true && ChestRigRuntime.Is(player, "TR17-2");
    internal static float Speed(Player? player) => Active(player) ? 1.5f : 1f;
    internal static bool IsMedical(Item? item) => item is MedsItemClass;
    internal static Player.MedsController? MedicalController(Player player, FirearmsAnimator animator)
    {
        // Native initialization registers this consumer before setting the
        // first multiplier, while HandsController can still be the old weapon.
        // Prefer the registered owner when a pooled animator is being reused.
        if (Native.Get(animator, "EventsConsumers") is IEnumerable consumers)
            foreach (var consumer in consumers)
                if (consumer is Player.MedsController controller && Owns(controller, player, animator)) return controller;
        return player.HandsController is Player.MedsController held && Owns(held, player, animator) ? held : null;
    }
    private static bool Owns(Player.MedsController controller, Player player, FirearmsAnimator animator) =>
        ReferenceEquals(Native.Get(controller, "firearmsAnimator_0"), animator) &&
        ReferenceEquals(BackpackRuntime.Actor(controller), player);
    internal static float MedicalAnimation(Player player, FirearmsAnimator animator) =>
        IsMedical(MedicalController(player, animator)?.Item) ? Speed(player) : 1f;
}
