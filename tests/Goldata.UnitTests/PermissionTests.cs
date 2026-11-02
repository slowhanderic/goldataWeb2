using Goldata.Application.Identity;
using Goldata.Application.Modules;
using Goldata.Application.Permissions;

namespace Goldata.UnitTests;

public class PermissionTests
{
    private static IReadOnlySet<string> For(string role, string rank, IEnumerable<UserOverride>? overrides = null)
    {
        var grants = DefaultPermissionMatrix.Build()[role];
        var readOnly = role.EndsWith("查阅", StringComparison.Ordinal);
        return EffectivePermissions.Compute(rank, readOnly, grants, overrides ?? []);
    }

    [Fact]
    public void Every_default_grant_is_a_valid_catalogue_code() =>
        DefaultPermissionMatrix.Build().SelectMany(kv => kv.Value).ShouldAllBe(g => PermissionCatalog.IsValid(g.Permission));

    [Fact]
    public void Management_director_is_admin_and_can_delete()
    {
        var p = For("管理", "总监");
        AdminRules.IsAdmin(p).ShouldBeTrue();
        p.ShouldContain("contracts:delete");
        p.ShouldContain("employees:read");
        p.ShouldContain(PermissionCatalog.LookupsAdmin);
    }

    [Fact]
    public void Hr_needs_finance_or_management_AND_director()
    {
        For("财务", "总监").ShouldContain("payrolls:read");
        For("财务", "员工").ShouldNotContain("payrolls:read");
        For("管理", "员工").ShouldNotContain("employees:read");
        For("采购", "总监").ShouldNotContain("employees:read");
    }

    [Fact]
    public void Finance_modules_are_management_and_finance_only()
    {
        For("财务", "员工").ShouldContain("payments:read");
        For("采购", "员工").ShouldNotContain("payments:read");
        For("业务", "员工").ShouldContain("contracts:update");
        For("业务", "员工").ShouldNotContain("contracts:delete");
    }

    [Fact]
    public void Retail_role_only_sees_retail_and_cost_estimator()
    {
        var p = For("家装", "员工");
        p.ShouldContain("projects-retail:read");
        p.ShouldContain("cost-estimates:read");
        p.ShouldNotContain("contracts:read");
    }

    [Fact]
    public void Todo_others_matches_legacy_home_page_rule()
    {
        For("采购", "员工").ShouldContain(PermissionCatalog.TodoAll);
        For("管理", "员工").ShouldContain(PermissionCatalog.TodoAll);
        For("业务", "总监").ShouldContain(PermissionCatalog.TodoAll);
        For("业务", "员工").ShouldNotContain(PermissionCatalog.TodoAll);
        For("采购", "员工").ShouldNotContain(PermissionCatalog.FeedRecent);
    }

    [Fact]
    public void Read_only_roles_never_get_write_permissions_even_by_override()
    {
        var p = For("采购查阅", "员工", [new("buy-orders:update", true)]);
        p.ShouldContain("buy-orders:read");
        p.ShouldNotContain("buy-orders:update");
        p.ShouldAllBe(code => !PermissionCatalog.Find(code)!.IsWrite);
    }

    [Fact]
    public void Deny_override_wins_over_role_grant() =>
        For("财务", "员工", [new("payments:read", false)]).ShouldNotContain("payments:read");

    [Fact]
    public void Allow_override_adds_a_permission() =>
        For("采购", "员工", [new("payments:read", true)]).ShouldContain("payments:read");

    [Fact]
    public void Explain_reports_sources()
    {
        var grants = new[] { new RoleGrant("*", "contracts:read"), new RoleGrant("总监", "contracts:delete") };
        var x = EffectivePermissions.Explain("总监", false, grants, [new UserOverride("contracts:read", true)]);
        x.Single(p => p.Permission == "contracts:read").Sources.ShouldBe(["role", "override"]);
        x.Single(p => p.Permission == "contracts:delete").Sources.ShouldBe(["role+rank"]);
    }

    [Fact]
    public void Workflow_action_exists_only_on_workflow_modules()
    {
        PermissionCatalog.IsValid("contracts:workflow").ShouldBeTrue();
        PermissionCatalog.IsValid("projects:workflow").ShouldBeFalse();
        PermissionCatalog.IsValid("inventory:upload").ShouldBeFalse();
    }

    [Fact]
    public void Module_registry_is_consistent()
    {
        ModuleRegistry.All.Select(m => m.Key).ShouldBeUnique();
        ModuleRegistry.All.SelectMany(m => m.Files).Select(f => f.Tag).ShouldBeUnique();
        // The 10 workflow tables of 002-spec §3.10
        ModuleRegistry.All.Where(m => m.Workflow).Select(m => m.Table).Order().ShouldBe(
            new[] { "additionals", "buy_orders", "buy_plans", "buy_requests", "contracts", "documents", "employees", "payrolls",
                    "receivable_applications", "settlements" }.Order());
    }

    [Fact]
    public void Seed_migration_0004_matches_the_default_matrix()
    {
        var path = Path.Combine(RepoRoot(), "db", "migrations", "goldata_app", "0004__seed_default_permissions.sql");
        var expected = DefaultMatrixSql.Render();
        if (Environment.GetEnvironmentVariable("GOLDATA_REGEN") == "1") File.WriteAllText(path, expected);
        File.Exists(path).ShouldBeTrue("Run GOLDATA_REGEN=1 dotnet test once to generate 0004.");
        File.ReadAllText(path).ReplaceLineEndings().ShouldBe(expected.ReplaceLineEndings());
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Goldata.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repo root not found");
    }
}
