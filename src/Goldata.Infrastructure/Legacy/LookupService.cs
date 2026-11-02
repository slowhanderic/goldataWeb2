using Dapper;
using Goldata.Application.Security;
using Goldata.Domain;
using Goldata.Infrastructure.Data;
using MySqlConnector;

namespace Goldata.Infrastructure.Legacy;

public sealed record LookupItem(int Id, string Item, string Value, string Data, int ParentId, int Ranking);

/// <summary>
/// Read access to <c>lookup_tables</c> (dropdowns). The root category <c>主要表单名称</c> lists the categories;
/// the staff list is category <c>公司员工</c>, where an empty <c>value</c> means "not shown in 转交" (legacy <c>show_history()</c>).
/// </summary>
public sealed class LookupService(Db db)
{
    public const string RootCategory = "主要表单名称";
    public const string StaffCategory = "公司员工";

    /// <summary>Department code suggested for a new staff entry; the admin can pick another (codes have no logic in PHP).</summary>
    public static string SuggestStaffCode(string role, string rank) => rank == "总监" ? "super" : role switch
    {
        "管理" => "admin", "财务" => "cw", "采购" => "buyer", "预算" => "ys", "家装" => "retail", "工程文员" => "gc", "业务" => "oa",
        _ => "oa",
    };

    public async Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken ct)
    {
        await using var c = await db.LegacyAsync(ct);
        return (await c.QueryAsync<string>(new CommandDefinition(
            "SELECT table_item FROM lookup_tables WHERE table_name = @root ORDER BY ranking, id",
            new { root = RootCategory }, cancellationToken: ct))).Select(LegacyText.Decode).ToList();
    }

    public async Task<IReadOnlyList<LookupItem>> ItemsAsync(string category, CancellationToken ct)
    {
        await using var c = await db.LegacyAsync(ct);
        return await ItemsAsync(c, null, category, ct);
    }

    public static async Task<IReadOnlyList<LookupItem>> ItemsAsync(MySqlConnection c, MySqlTransaction? tx, string category, CancellationToken ct) =>
        (await c.QueryAsync<LookupItem>(new CommandDefinition("""
            SELECT id, table_item AS Item, value AS Value, data AS Data, parent_id AS ParentId, ranking AS Ranking
            FROM lookup_tables WHERE table_name = @category ORDER BY ranking, id
            """, new { category = LegacyText.EncodeCompat(category) }, tx, cancellationToken: ct)))
        .Select(i => i with { Item = LegacyText.Decode(i.Item), Value = LegacyText.Decode(i.Value), Data = LegacyText.Decode(i.Data) })
        .ToList();

    public async Task<IReadOnlyList<string>> ActiveStaffAsync(CancellationToken ct) =>
        (await ItemsAsync(StaffCategory, ct)).Where(i => i.Value != "").Select(i => i.Item).ToList();

    public async Task<IReadOnlyList<string>> StaffCodesAsync(CancellationToken ct) =>
        (await ItemsAsync(StaffCategory, ct)).Select(i => i.Value).Where(v => v != "").Distinct().Order().ToList();

    /// <summary>Adds the name to 公司员工, or re-activates it, with an update_logs entry (REQ-ADM-2).</summary>
    public static async Task EnsureStaffAsync(MySqlConnection c, MySqlTransaction tx, string displayName, string code, ICurrentUser actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) throw AppError.Validation("请选择部门代码");
        var existing = (await ItemsAsync(c, tx, StaffCategory, ct)).FirstOrDefault(i => i.Item == displayName);
        if (existing is not null)
        {
            if (existing.Value != "") return;
            await c.ExecuteAsync(new CommandDefinition("UPDATE lookup_tables SET value = @code WHERE id = @id",
                new { code, id = existing.Id }, tx, cancellationToken: ct));
            await LegacyAuditWriter.WriteAsync(c, tx, "lookup_tables", existing.Id,
                LegacyAuditWriter.Render("UPDATE", "lookup_tables", [new("value", code)], $"id = {existing.Id}"), actor, LegacyAuditWriter.Updated, ct);
            return;
        }

        var parentId = await c.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT id FROM lookup_tables WHERE table_name = @root AND table_item = @staff ORDER BY id LIMIT 1",
            new { root = RootCategory, staff = StaffCategory }, tx, cancellationToken: ct))
            ?? throw new AppError(500, "LOOKUP", "找不到「公司员工」下拉分类");
        var ranking = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COALESCE(MAX(ranking), 0) + 1 FROM lookup_tables WHERE table_name = @staff",
            new { staff = StaffCategory }, tx, cancellationToken: ct));
        var values = new Dictionary<string, object?>
        {
            ["table_name"] = StaffCategory, ["table_item"] = LegacyText.EncodeCompat(displayName),
            ["parent_id"] = parentId, ["ranking"] = ranking, ["value"] = code, ["data"] = "",
        };
        var id = await c.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO lookup_tables (table_name, table_item, parent_id, ranking, value, data)
            VALUES (@table_name, @table_item, @parent_id, @ranking, @value, @data);
            SELECT LAST_INSERT_ID();
            """, values, tx, cancellationToken: ct));
        await LegacyAuditWriter.WriteAsync(c, tx, "lookup_tables", id,
            LegacyAuditWriter.Render("INSERT", "lookup_tables", values), actor, LegacyAuditWriter.Added, ct);
    }

    /// <summary>Hides the name from the 转交 dropdown (value = ''), as legacy does for leavers. The row is kept.</summary>
    public static async Task DeactivateStaffAsync(MySqlConnection c, MySqlTransaction tx, string displayName, ICurrentUser actor, CancellationToken ct)
    {
        var existing = (await ItemsAsync(c, tx, StaffCategory, ct)).FirstOrDefault(i => i.Item == displayName);
        if (existing is null || existing.Value == "") return;
        await c.ExecuteAsync(new CommandDefinition("UPDATE lookup_tables SET value = '' WHERE id = @id",
            new { id = existing.Id }, tx, cancellationToken: ct));
        await LegacyAuditWriter.WriteAsync(c, tx, "lookup_tables", existing.Id,
            LegacyAuditWriter.Render("UPDATE", "lookup_tables", [new("value", "")], $"id = {existing.Id}"), actor, LegacyAuditWriter.Updated, ct);
    }
}
