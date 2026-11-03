using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace EscapeFromBorderlands.Server;

internal sealed class FenceStockPolicy
{
    private readonly HashSet<string> Blocked;
    internal IReadOnlySet<string> BlockedTemplates => Blocked;

    internal FenceStockPolicy(LegendaryCatalog catalog, bool allowEfb)
    {
        // Expanded storage and mystery templates are already included by Load.
        // Retired aliases and purchase-only previews never become Fence stock.
        Blocked = catalog.Variants.Where(v => !allowEfb || v.IsMysteryFleaOffer)
            .Select(v => v.TemplateId).Concat(catalog.LegacyAliases.Select(v => v.TemplateId))
            .ToHashSet(StringComparer.Ordinal);
    }

    internal void AddBlacklist(HashSet<MongoId> blacklist)
    {
        foreach (var id in Blocked) blacklist.Add(new MongoId(id));
    }

    // The caller passes inventory items, not necessarily just the sold subtree.
    // Reject only the offered root and descendants; never mutate that inventory.
    internal bool BlocksResale(IEnumerable<Item> items, Item root)
    {
        if (Blocked.Contains(root.Template.ToString())) return true;
        var children = Children(items);
        var pending = new Stack<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        pending.Push(root.Id.ToString());
        while (pending.TryPop(out var id))
        {
            if (!visited.Add(id) || !children.TryGetValue(id, out var entries)) continue;
            foreach (var item in entries)
            {
                if (Blocked.Contains(item.Template.ToString())) return true;
                pending.Push(item.Id.ToString());
            }
        }
        return false;
    }

    internal int Filter(TraderAssort? assort)
    {
        if (assort?.Items is not { Count: > 0 } items || !items.Any(i => Blocked.Contains(i.Template.ToString()))) return 0;
        var byId = new Dictionary<string, Item>(StringComparer.Ordinal);
        foreach (var item in items) byId.TryAdd(item.Id.ToString(), item);
        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items.Where(i => Blocked.Contains(i.Template.ToString())))
        {
            var root = item;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (visited.Add(root.Id.ToString()) && root.ParentId is { } parent && byId.TryGetValue(parent, out var ancestor)) root = ancestor;
            roots.Add(root.Id.ToString());
        }
        var children = Children(items);
        var remove = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(roots);
        while (pending.TryPop(out var id))
        {
            if (!remove.Add(id) || !children.TryGetValue(id, out var entries)) continue;
            foreach (var item in entries) pending.Push(item.Id.ToString());
        }
        items.RemoveAll(item => remove.Contains(item.Id.ToString()));
        if (assort.BarterScheme is { } barter)
            foreach (var id in barter.Keys.Where(id => remove.Contains(id.ToString())).ToArray()) barter.Remove(id);
        if (assort.LoyalLevelItems is { } levels)
            foreach (var id in levels.Keys.Where(id => remove.Contains(id.ToString())).ToArray()) levels.Remove(id);
        return roots.Count;
    }

    private static Dictionary<string, List<Item>> Children(IEnumerable<Item> items)
    {
        var children = new Dictionary<string, List<Item>>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (item.ParentId is not { } parent) continue;
            if (!children.TryGetValue(parent, out var entries)) children[parent] = entries = [];
            entries.Add(item);
        }
        return children;
    }
}
