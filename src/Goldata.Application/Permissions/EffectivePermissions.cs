namespace Goldata.Application.Permissions;

/// <summary>A role grant: <c>Rank = "*"</c> applies to all ranks.</summary>
public sealed record RoleGrant(string Rank, string Permission);

public sealed record UserOverride(string Permission, bool Allow);

public sealed record PermissionSource(string Permission, IReadOnlyList<string> Sources);

/// <summary>
/// Effective permissions = (role grants for '*') ∪ (role grants for the user's rank) ∪ (override allows)
/// − (override denies), then minus every write permission if the role is read-only (002-spec §5.2).
/// </summary>
public static class EffectivePermissions
{
    public const string AllRanks = "*";

    public static IReadOnlySet<string> Compute(
        string rank, bool roleIsReadOnly, IEnumerable<RoleGrant> roleGrants, IEnumerable<UserOverride> overrides) =>
        Explain(rank, roleIsReadOnly, roleGrants, overrides).Select(p => p.Permission).ToHashSet(StringComparer.Ordinal);

    public static IReadOnlyList<PermissionSource> Explain(
        string rank, bool roleIsReadOnly, IEnumerable<RoleGrant> roleGrants, IEnumerable<UserOverride> overrides)
    {
        var sources = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string p, string src)
        {
            if (!sources.TryGetValue(p, out var l)) sources[p] = l = [];
            if (!l.Contains(src)) l.Add(src);
        }

        foreach (var g in roleGrants)
        {
            if (g.Rank == AllRanks) Add(g.Permission, "role");
            else if (g.Rank == rank) Add(g.Permission, "role+rank");
        }
        var overrideList = overrides.ToList();
        foreach (var o in overrideList.Where(o => o.Allow)) Add(o.Permission, "override");
        foreach (var o in overrideList.Where(o => !o.Allow)) sources.Remove(o.Permission);

        if (roleIsReadOnly)
            foreach (var p in sources.Keys.ToList())
                if (PermissionCatalog.Find(p) is { IsWrite: true }) sources.Remove(p);

        // Unknown codes (e.g. a module removed from the catalogue) are ignored.
        return sources.Where(kv => PermissionCatalog.IsValid(kv.Key))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new PermissionSource(kv.Key, kv.Value))
            .ToList();
    }
}
