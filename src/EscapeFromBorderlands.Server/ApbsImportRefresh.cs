using System.Reflection;
using HarmonyLib;

namespace EscapeFromBorderlands.Server;

// APBS SaveConfig/ReloadConfig replace tier data and await its importer. Wrap
// that task so the caller cannot observe completion before EFB reconciles it.
// Do not start the server/importer here, retain game references, or replay OnLoad.
internal static class ApbsImportRefresh
{
    private static readonly Dictionary<MethodBase, Func<Task>> Refresh = new();
    private static readonly object InstallLock = new();

    internal static void Install(Type importer, Func<Task> reconcile)
    {
        var method = importer.GetMethod("OnLoadAsync", new[] { typeof(CancellationToken) })
            ?? throw new MissingMethodException(importer.FullName, "OnLoadAsync(CancellationToken)");
        if (method.ReturnType != typeof(Task)) throw new InvalidOperationException("Unsupported APBS importer task.");
        lock (InstallLock)
        {
            if (Refresh.ContainsKey(method)) { Refresh[method] = reconcile; return; }
            Refresh[method] = reconcile;
            try
            {
                new Harmony("com.tkw.escapefromborderlands.server.apbsreload").Patch(method,
                    postfix: new HarmonyMethod(typeof(ApbsImportRefresh), nameof(AfterImport)) { priority = Priority.Last });
            }
            catch { Refresh.Remove(method); throw; }
        }
    }

    private static void AfterImport(MethodBase __originalMethod, ref Task __result)
    {
        Func<Task> reconcile;
        lock (InstallLock) reconcile = Refresh[__originalMethod];
        __result = AfterCompletion(__result, reconcile);
    }

    internal static async Task AfterCompletion(Task import, Func<Task> reconcile)
    {
        await import.ConfigureAwait(false);
        // Faults/cancellation must propagate without normalizing a partial import.
        await reconcile().ConfigureAwait(false);
    }
}
