using System.Security.Cryptography;
using System.Text;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Services;

namespace EscapeFromBorderlands.Server;

// Remove the retired prototype offer if it remains in a persisted/modded assort.
internal static class ReusableGrenadeTestOffer
{
    internal static readonly MongoId Prapor = new("54cb50c76803fa8b248b4571");
    internal static MongoId OfferId(string template) => new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("efb:grenade-test:" + template)))[..24].ToLowerInvariant());
    internal static void Install(DatabaseService database, LegendaryCatalog catalog)
    {
        if (!database.GetTraders().TryGetValue(Prapor, out var trader) || trader.Assort is not { } assort) return;
        foreach (var definition in catalog.Variants.Where(v => v.GrenadeChargesPerRaid > 0))
            Remove(assort, definition);
    }
    internal static void Remove(TraderAssort assort, VariantDefinition definition)
    {
        var offer = OfferId(definition.TemplateId);
        assort.Items.RemoveAll(i => i.Id == offer);
        assort.BarterScheme.Remove(offer);
        assort.LoyalLevelItems.Remove(offer);
    }
}
