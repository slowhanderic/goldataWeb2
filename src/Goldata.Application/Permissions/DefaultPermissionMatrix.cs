using Goldata.Application.Modules;

namespace Goldata.Application.Permissions;

/// <summary>
/// The default matrix that reproduces the legacy access rules (001 §3, 002-spec §5.2).
/// Used by 恢复默认 on seeded roles and to generate <c>db/migrations/goldata_app/0004__seed_default_permissions.sql</c>
/// (see <c>tests/Goldata.UnitTests/DefaultMatrixSqlTests</c>, which fails if the two drift apart).
/// </summary>
public static class DefaultPermissionMatrix
{
    public const string Director = "总监";

    private static readonly string[] Six = ["管理", "财务", "业务", "采购", "工程文员", "预算"];
    private static readonly string[] SixPlusRetail = [.. Six, "家装"];
    private static readonly string[] Finance = ["管理", "财务"];
    private static readonly string[] AllLegacyRoles =
        ["管理", "业务", "财务", "采购", "预算", "家装", "工程文员", "采购查阅", "工程查阅", "预算查阅"];

    private static readonly string[] WorkActions = ["read", "create", "update", "link", "workflow", "export", "upload", "file_delete"];

    private static readonly string[] GongzhuangModules =
        ["projects", "budgets", "contracts", "receivable-applications", "additionals", "documents", "settlements"];
    private static readonly string[] CaigouModules =
        ["suppliers", "buy-plans", "buy-requests", "buy-orders", "buy-receipts", "inventory", "deliveries", "catalogue"];
    private static readonly string[] JiazhuangModules = ["projects-retail", "quotes-retail"];
    private static readonly string[] CaiwuModules = ["sales-invoices", "buy-invoices", "receiveds", "payments", "labor-settlements"];
    private static readonly string[] HrModules = ["employees", "payrolls"];

    /// <summary>Role name → grants. Only valid catalogue codes are emitted.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<RoleGrant>> Build()
    {
        var map = AllLegacyRoles.ToDictionary(r => r, _ => new HashSet<RoleGrant>());
        void Grant(IEnumerable<string> roles, IEnumerable<string> modules, IEnumerable<string> actions, string rank = EffectivePermissions.AllRanks)
        {
            foreach (var role in roles)
            foreach (var module in modules)
            foreach (var action in actions)
            {
                var code = PermissionCatalog.Code(module, action);
                if (PermissionCatalog.IsValid(code)) map[role].Add(new RoleGrant(rank, code));
            }
        }
        void Special(IEnumerable<string> roles, string code, string rank = EffectivePermissions.AllRanks)
        {
            foreach (var role in roles) map[role].Add(new RoleGrant(rank, code));
        }

        Grant(Six, GongzhuangModules, WorkActions);
        Grant(Six, CaigouModules, WorkActions);
        Grant(SixPlusRetail, JiazhuangModules, WorkActions);
        Grant(Finance, CaiwuModules, WorkActions);
        Special(Finance, PermissionCatalog.ReportsFinance);
        Grant(SixPlusRetail, ["cost-estimates"], ["read", "create", "update"]);

        // 人事: (管理 or 财务) AND 总监: the only area the legacy app also checks per page.
        Grant(Finance, HrModules, WorkActions, Director);
        Special(Finance, PermissionCatalog.ReportsSalary, Director);

        // 管理 + 总监: delete master records, lookups, user and role admin (legacy delete_row / lookup_tables).
        var allGenericModules = ModuleRegistry.All.Select(m => m.Key);
        Grant(["管理"], allGenericModules, ["delete"], Director);
        Special(["管理"], PermissionCatalog.LookupsAdmin, Director);
        Special(["管理"], PermissionCatalog.UsersAdmin, Director);
        Special(["管理"], PermissionCatalog.RolesAdmin, Director);

        // Home page: other people's to-dos = 管理, any 总监, 采购; recent changes = 管理, any 总监 (legacy admin-index).
        Special(["管理", "采购"], PermissionCatalog.TodoAll);
        Special(AllLegacyRoles, PermissionCatalog.TodoAll, Director);
        Special(["管理"], PermissionCatalog.FeedRecent);
        Special(AllLegacyRoles, PermissionCatalog.FeedRecent, Director);

        // Read-only roles (Q12): subsets taken from the commented-out legacy menu.
        Grant(["采购查阅"], CaigouModules, ["read", "export"]);
        Grant(["工程查阅"], ["buy-orders", "documents"], ["read", "export"]);
        Grant(["预算查阅"], GongzhuangModules, ["read", "export"]);

        // MCP (Q10 pending): read for 管理 only until the data-governance decision is made; no write anywhere.
        Special(["管理"], PermissionCatalog.McpRead);

        return map.ToDictionary(kv => kv.Key,
            kv => (IReadOnlyList<RoleGrant>)kv.Value.OrderBy(g => g.Rank, StringComparer.Ordinal)
                .ThenBy(g => g.Permission, StringComparer.Ordinal).ToList());
    }
}
