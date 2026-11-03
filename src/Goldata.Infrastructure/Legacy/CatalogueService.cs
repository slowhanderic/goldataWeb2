using Dapper;
using Goldata.Domain;
using Goldata.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;

namespace Goldata.Infrastructure.Legacy;

/// <summary>A product = a 型号 leaf of the 4-level catalogue; <c>*_items.product_id</c> is its lookup id.</summary>
public sealed record Product(long Id, string Cat, string SubCat, string Item, string Type, string Unit)
{
    public string Path => $"{Cat} / {SubCat} / {Item} / {Type}";
}

public sealed record CatalogueNode(long Id, string Name, int Level, long? ParentId, int Children);

/// <summary>
/// Product catalogue (M17, 002-spec §3.11): <c>lookup_tables</c> rows below the root category <c>产品类别</c>,
/// 类别 → 细类 → 品名 → 型号 linked by <c>parent_id</c>; the leaf's <c>value</c> is the unit.
/// Replaces <c>dropdown/ajax/*-ep.php</c>. Cached for 5 minutes (a few thousand leaves).
/// </summary>
public sealed class CatalogueService(Db db, IMemoryCache cache)
{
    public const string RootCategory = "产品类别";
    private const string CacheKey = "catalogue";

    private sealed record Snapshot(IReadOnlyDictionary<long, Product> Products, IReadOnlyList<CatalogueNode> Nodes);

    public static void Invalidate(IMemoryCache cache) => cache.Remove(CacheKey);

    private async Task<Snapshot> LoadAsync(CancellationToken ct) => (await cache.GetOrCreateAsync(CacheKey, async e =>
    {
        e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
        await using var c = await db.LegacyAsync(ct);
        var rows = (await c.QueryAsync<(long Id, string Name, long ParentId, string Value, int Level)>(new CommandDefinition("""
            SELECT CAST(c.id AS SIGNED), c.table_item, 0, '', 1 FROM lookup_tables c WHERE c.table_name = @root
            UNION ALL
            SELECT CAST(s.id AS SIGNED), s.table_item, CAST(s.parent_id AS SIGNED), '', 2 FROM lookup_tables c
              JOIN lookup_tables s ON s.parent_id = c.id WHERE c.table_name = @root
            UNION ALL
            SELECT CAST(i.id AS SIGNED), i.table_item, CAST(i.parent_id AS SIGNED), '', 3 FROM lookup_tables c
              JOIN lookup_tables s ON s.parent_id = c.id JOIN lookup_tables i ON i.parent_id = s.id WHERE c.table_name = @root
            UNION ALL
            SELECT CAST(t.id AS SIGNED), t.table_item, CAST(t.parent_id AS SIGNED), t.value, 4 FROM lookup_tables c
              JOIN lookup_tables s ON s.parent_id = c.id JOIN lookup_tables i ON i.parent_id = s.id
              JOIN lookup_tables t ON t.parent_id = i.id WHERE c.table_name = @root
            """, new { root = RootCategory }, cancellationToken: ct))).ToList();

        var byId = rows.ToDictionary(r => r.Id);
        var childCount = rows.Where(r => r.Level > 1).GroupBy(r => r.ParentId).ToDictionary(g => g.Key, g => g.Count());
        string Name(long id) => byId.TryGetValue(id, out var r) ? LegacyText.Decode(r.Name).Trim() : "";   // some names carry trailing spaces
        var products = new Dictionary<long, Product>();
        foreach (var t in rows.Where(r => r.Level == 4))
        {
            var i = byId[t.ParentId];
            var s = byId[i.ParentId];
            products[t.Id] = new Product(t.Id, Name(s.ParentId), Name(s.Id), Name(i.Id), LegacyText.Decode(t.Name).Trim(), LegacyText.Decode(t.Value).Trim());
        }
        var nodes = rows.Select(r => new CatalogueNode(r.Id, LegacyText.Decode(r.Name).Trim(), r.Level, r.Level == 1 ? null : r.ParentId,
            childCount.GetValueOrDefault(r.Id))).ToList();
        return new Snapshot(products, nodes);
    }))!;

    public async Task<Product?> GetAsync(long id, CancellationToken ct) => (await LoadAsync(ct)).Products.GetValueOrDefault(id);

    public async Task<IReadOnlyDictionary<long, Product>> GetManyAsync(IEnumerable<long> ids, CancellationToken ct)
    {
        var all = (await LoadAsync(ct)).Products;
        return ids.Distinct().Where(all.ContainsKey).ToDictionary(id => id, id => all[id]);
    }

    /// <summary>Every search word must appear somewhere in the path (类别/细类/品名/型号), e.g. "大金 风管".</summary>
    public async Task<IReadOnlyList<Product>> SearchAsync(string? q, int limit, CancellationToken ct)
    {
        var words = (q ?? "").Split([' ', '　', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];
        return (await LoadAsync(ct)).Products.Values
            .Where(p => words.All(w => p.Path.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p.Path, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, 100))
            .ToList();
    }

    /// <summary>Children of a node (null = the 类别 level) for the browse dialog.</summary>
    public async Task<IReadOnlyList<CatalogueNode>> ChildrenAsync(long? parentId, CancellationToken ct) =>
        (await LoadAsync(ct)).Nodes.Where(n => n.ParentId == parentId).OrderBy(n => n.Name, StringComparer.Ordinal).ToList();
}
