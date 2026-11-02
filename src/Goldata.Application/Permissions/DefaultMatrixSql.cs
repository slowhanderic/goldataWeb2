using System.Text;

namespace Goldata.Application.Permissions;

/// <summary>
/// Renders <see cref="DefaultPermissionMatrix"/> as the seed migration
/// <c>db/migrations/goldata_app/0004__seed_default_permissions.sql</c>. A unit test compares the file with this output,
/// so the SQL seed and the 恢复默认 behaviour can't drift apart. Regenerate with <c>GOLDATA_REGEN=1 dotnet test</c>.
/// </summary>
public static class DefaultMatrixSql
{
    public static string Render()
    {
        var sb = new StringBuilder();
        sb.Append("""
            -- Purpose : Seed the default role × rank permission matrix that reproduces the legacy access rules.
            --           GENERATED from DefaultPermissionMatrix.cs — do not edit by hand. Later changes = a new script.
            -- Ref     : 001 §3, REQ-ROLE-5, 002-spec §5.2
            -- Author  : GoldataWeb2, 2026-11-02 (generated)
            -- Rollback: forward-only (admins can change the matrix on the 角色与权限 page; 恢复默认 restores these rows).

            """);
        foreach (var (role, grants) in DefaultPermissionMatrix.Build().OrderBy(kv => RoleOrder(kv.Key)))
        {
            if (grants.Count == 0) continue;
            sb.Append('\n').Append("-- ").Append(role).Append(" (").Append(grants.Count).Append(" grants)\n");
            sb.Append("INSERT INTO app_role_permissions (role_id, `rank`, permission)\nSELECT r.id, x.rk, x.perm FROM app_roles r JOIN (\n");
            for (var i = 0; i < grants.Count; i++)
            {
                var g = grants[i];
                sb.Append(i == 0 ? "  SELECT " : "  UNION ALL SELECT ")
                  .Append(Lit(g.Rank)).Append(i == 0 ? " AS rk, " : ", ").Append(Lit(g.Permission)).Append(i == 0 ? " AS perm" : "").Append('\n');
            }
            sb.Append(") x\nWHERE r.name = ").Append(Lit(role)).Append(";\n");
        }
        return sb.ToString();
    }

    private static readonly string[] Order = ["管理", "业务", "财务", "采购", "预算", "家装", "工程文员", "采购查阅", "工程查阅", "预算查阅"];
    private static int RoleOrder(string role) => Array.IndexOf(Order, role) is var i and >= 0 ? i : 99;

    private static string Lit(string s) => "'" + s.Replace("'", "''") + "'";
}
