using System.Reflection;
using HarmonyLib;
using SPTarkov.Server.Core.Generators;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Commerce;

namespace EscapeFromBorderlands.Server;

internal static class FenceTrading
{
    private const string HarmonyId = "com.tKwNw.escapefromborderlands.server.fence";
    private static FenceStockPolicy? Policy;
    private static TradersTable? Traders;
    private static FenceService? Fence;
    private static bool Installed;

    internal static void Install(LegendaryCatalog catalog, RarityConfiguration config,
        TraderConfig traderConfig, TradersTable traders, FenceService fence)
    {
        Policy = new FenceStockPolicy(catalog, config.AllowFenceToSellEfbItems);
        Policy.AddBlacklist(traderConfig.Fence.Blacklist);
        Traders = traders;
        Fence = fence;
        if (!Installed)
        {
            // Use the installed SPT signatures rather than replacing the trader or
            // intercepting a player's sale/payment transaction.
            var resale = Require(typeof(FenceService), nameof(FenceService.AddItemsToFenceAssort), typeof(void), typeof(IEnumerable<Item>), typeof(Item));
            var offers = Require(typeof(FenceService), nameof(FenceService.GetFenceAssorts), typeof(TraderAssort), typeof(PmcData));
            var raw = Require(typeof(FenceService), nameof(FenceService.GetRawFenceAssorts), typeof(TraderAssort));
            var generated = Require(typeof(FenceService), nameof(FenceService.GenerateFenceAssorts), typeof(void));
            var bases = Require(typeof(FenceBaseAssortGenerator), nameof(FenceBaseAssortGenerator.GenerateFenceBaseAssorts), typeof(void));
            var harmony = new Harmony(HarmonyId);
            try
            {
                harmony.Patch(resale, prefix: new HarmonyMethod(typeof(FenceTrading), nameof(BeforeResale)));
                foreach (var method in new[] { offers, raw })
                    harmony.Patch(method,
                        prefix: new HarmonyMethod(typeof(FenceTrading), nameof(BeforeOffers)),
                        postfix: new HarmonyMethod(typeof(FenceTrading), nameof(AfterOffers)) { priority = Priority.Last });
                harmony.Patch(generated, postfix: new HarmonyMethod(typeof(FenceTrading), nameof(AfterGeneration)) { priority = Priority.Last });
                harmony.Patch(bases, postfix: new HarmonyMethod(typeof(FenceTrading), nameof(AfterBaseGeneration)) { priority = Priority.Last });
                Installed = true;
            }
            catch
            {
                harmony.UnpatchSelf();
                throw;
            }
        }
        CleanAll();
    }

    private static MethodInfo Require(Type type, string name, Type result, params Type[] args)
    {
        var method = AccessTools.DeclaredMethod(type, name, args);
        return method?.ReturnType == result ? method : throw new MissingMethodException($"Unsupported SPT Fence method: {type.FullName}.{name}");
    }

    private static bool BeforeResale(IEnumerable<Item> __0, Item __1) => Policy?.BlocksResale(__0, __1) != true;
    private static void BeforeOffers(FenceService __instance) => CleanStored(__instance);
    private static void AfterOffers(TraderAssort __result) => Policy?.Filter(__result);
    private static void AfterGeneration(FenceService __instance) => CleanStored(__instance);
    private static void AfterBaseGeneration() => CleanAll();

    private static void CleanStored(FenceService fence)
    {
        Policy?.Filter(fence.GetMainFenceAssort());
        Policy?.Filter(fence.GetDiscountFenceAssort());
    }

    private static void CleanAll()
    {
        if (Traders?.TryGetValue(TradersId, out var trader) == true) Policy?.Filter(trader.Assort);
        if (Fence is not null) CleanStored(Fence);
    }

    private static readonly SPTarkov.Server.Core.Models.Common.MongoId TradersId = SPTarkov.Server.Core.Models.Enums.Traders.FENCE;
}
