using EFT.InventoryLogic;

namespace EscapeFromBorderlands.Client;

internal static class InventoryPredicates
{
    internal static bool HasFoodVariety(Item bag)
    {
        string? first = null;
        var found = false;
        foreach (var item in bag.GetAllItems())
        {
            if (Native.Component<FoodDrinkComponent>(item) is null || item.Template.ParentId == "5448e8d64bdc2dce718b4568")
                continue;
            if (found && first != item.StringTemplateId)
                return true;
            first = item.StringTemplateId;
            found = true;
        }
        return false;
    }
}
