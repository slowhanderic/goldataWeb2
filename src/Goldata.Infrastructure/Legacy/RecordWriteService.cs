using Dapper;
using Goldata.Application.Modules;
using Goldata.Application.Permissions;
using Goldata.Application.Records;
using Goldata.Application.Security;
using Goldata.Domain;
using Goldata.Infrastructure.Data;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Goldata.Infrastructure.Legacy;

public sealed record SaveResult(long Id, string ETag);

public sealed record DependentRef(string Table, string Column, long Count, string? Module, string? ModuleTitle);

public sealed record RelatedRow(long Id, string Title, string? Status);

public sealed record RelatedGroup(string Module, string Title, string Column, string Label, long Total, IReadOnlyList<RelatedRow> Rows);

public sealed record ParentRef(string Column, string Label, string Module, string ModuleTitle, long Id, string Title);

public sealed record RelatedResult(IReadOnlyList<ParentRef> Parents, IReadOnlyList<RelatedGroup> Children);

/// <summary>
/// Create / update / delete / 转交 for the generic modules (REQ-EDIT-1…6, REQ-WF-1/2, REQ-DEL, REQ-AUD-1).
/// Values are written exactly like the legacy app (WriteRules); every statement is logged in update_logs
/// in the same transaction; updates are protected by the row-hash ETag (002-spec §3.4).
/// </summary>
public sealed class RecordWriteService(Db db, SchemaCache schemas, RecordService records, LookupService lookups, IOptions<LegacyCompatOptions> compat)
{
    private readonly LegacyCompatOptions _compat = compat.Value;

    // ---------- create ----------

    public async Task<SaveResult> CreateAsync(string key, IReadOnlyDictionary<string, string?> input, string? parentKey, long? parentId,
        ICurrentUser user, CancellationToken ct)
    {
        var m = await records.AuthorizeAsync(key, user, "create", ct);
        var schema = await schemas.GetAsync(m.Table, ct);
        if (WriteRules.EditableFields(m, schema).Count == 0) throw AppError.Validation("该模块暂不支持在新系统中新增");
        var values = WriteRules.Normalize(m, schema, input, isCreate: true, _compat.Utf8mb4, _compat.EncodeHtmlOnWrite);

        // 录入员 defaults to the current user, as the legacy form preselects it.
        if (schema.Find("user_name") is not null && values.TryGetValue("user_name", out var un) && string.IsNullOrEmpty(un as string))
            values["user_name"] = LegacyText.EncodeCompat(user.DisplayName);
        else if (schema.Find("user_name") is not null && !values.ContainsKey("user_name"))
            values["user_name"] = LegacyText.EncodeCompat(user.DisplayName);

        await using var c = await db.LegacyAsync(ct);
        await using var tx = await c.BeginAsync(ct);

        if (parentKey is not null)
        {
            var link = (ModuleRelations.CreateFrom.GetValueOrDefault(m.Key) ?? []).FirstOrDefault(p => p.Parent == parentKey);
            if (link == default) throw AppError.Validation("不支持从该类记录新建");
            var parent = await records.AuthorizeAsync(parentKey, user, "read", ct);
            if (parentId is null || !await ExistsAsync(c, tx, parent.Table, parentId.Value, ct)) throw AppError.NotFound("上级记录");
            values[link.Column] = parentId.Value;
        }

        // Strict mode: every NOT NULL column without a default must get the legacy "empty" value.
        foreach (var col in schema.Columns.Where(x => !x.Nullable && !x.HasDefault && x.Name != "id" && !values.ContainsKey(x.Name)))
            values[col.Name] = EmptyFor(col);

        var cols = values.Keys.ToList();
        var p = new DynamicParameters();
        for (var i = 0; i < cols.Count; i++) p.Add("v" + i, values[cols[i]]);
        var sql = $"INSERT INTO {Q(m.Table)} ({string.Join(", ", cols.Select(Q))}) VALUES ({string.Join(", ", cols.Select((_, i) => "@v" + i))}); SELECT LAST_INSERT_ID();";
        var id = await c.ExecuteScalarAsync<long>(new CommandDefinition(sql, p, tx, cancellationToken: ct));

        await LegacyAuditWriter.WriteAsync(c, tx, m.Table, id, LegacyAuditWriter.Render("INSERT", m.Table, values), user, LegacyAuditWriter.Added, ct);
        await tx.CommitAsync(ct);
        var detail = await records.GetAsync(key, id, user, ct);
        return new SaveResult(id, detail.ETag);
    }

    // ---------- update ----------

    public async Task<SaveResult> UpdateAsync(string key, long id, IReadOnlyDictionary<string, string?> input, string? ifMatch,
        ICurrentUser user, CancellationToken ct)
    {
        var m = await records.AuthorizeAsync(key, user, "update", ct);
        if (string.IsNullOrEmpty(ifMatch)) throw new AppError(428, "IF_MATCH_REQUIRED", "缺少版本信息（If-Match），请刷新页面后重试");
        var schema = await schemas.GetAsync(m.Table, ct);
        var values = WriteRules.Normalize(m, schema, input, isCreate: false, _compat.Utf8mb4, _compat.EncodeHtmlOnWrite);

        await using var c = await db.LegacyAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        var raw = (IDictionary<string, object?>?)await c.QuerySingleOrDefaultAsync(new CommandDefinition(
            $"SELECT * FROM {Q(m.Table)} WHERE `id` = @id FOR UPDATE", new { id }, tx, cancellationToken: ct))
            ?? throw AppError.NotFound();

        // Same canonical form as GET → ETag; catches edits made in the new app AND in PHP since the page was opened.
        var visible = ListQueryBuilder.VisibleColumns(m, schema).Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = RowMapper.Map(raw.Where(kv => visible.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value), schema);
        var items = await ItemsAsync(c, tx, m, id, ct);
        if (RecordService.ETag(current, items) != ifMatch)
            throw AppError.Conflict("CONCURRENT_EDIT", "该记录已被其他人修改（新系统或旧系统），请查看最新内容后再保存", new { record = current });

        var changes = WriteRules.Changes(values, raw.ToDictionary(kv => kv.Key, kv => kv.Value));
        if (changes.Count > 0)
        {
            var cols = changes.Keys.ToList();
            var p = new DynamicParameters(new { id });
            for (var i = 0; i < cols.Count; i++) p.Add("v" + i, changes[cols[i]]);
            await c.ExecuteAsync(new CommandDefinition(
                $"UPDATE {Q(m.Table)} SET {string.Join(", ", cols.Select((col, i) => $"{Q(col)} = @v{i}"))} WHERE `id` = @id", p, tx, cancellationToken: ct));
            await LegacyAuditWriter.WriteAsync(c, tx, m.Table, id, LegacyAuditWriter.Render("UPDATE", m.Table, changes, $"id = {id}"), user,
                LegacyAuditWriter.Updated, ct);
        }
        await tx.CommitAsync(ct);
        var detail = await records.GetAsync(key, id, user, ct);
        return new SaveResult(id, detail.ETag);
    }

    // ---------- delete ----------

    public async Task<IReadOnlyList<DependentRef>> DependentsAsync(string key, long id, ICurrentUser user, CancellationToken ct)
    {
        var m = await records.AuthorizeAsync(key, user, "read", ct);
        await using var c = await db.LegacyAsync(ct);
        return await DependentsAsync(c, null, m, id, ct);
    }

    public async Task DeleteAsync(string key, long id, ICurrentUser user, CancellationToken ct)
    {
        var m = await records.AuthorizeAsync(key, user, "delete", ct);
        await using var c = await db.LegacyAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        if (!await ExistsAsync(c, tx, m.Table, id, ct, forUpdate: true)) throw AppError.NotFound();
        var deps = await DependentsAsync(c, tx, m, id, ct);
        if (deps.Count > 0)
            throw AppError.Conflict("HAS_DEPENDENTS", "该记录仍有关联记录、明细或附件，不能删除（请先解除关联或删除附件）", deps);
        await c.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q(m.Table)} WHERE `id` = @id", new { id }, tx, cancellationToken: ct));
        await LegacyAuditWriter.WriteAsync(c, tx, m.Table, id, LegacyAuditWriter.Render("DELETE", m.Table, [], $"id = {id}"), user,
            LegacyAuditWriter.Deleted, ct);
        await tx.CommitAsync(ct);
    }

    private async Task<IReadOnlyList<DependentRef>> DependentsAsync(MySqlConnection c, MySqlTransaction? tx, ModuleDefinition m, long id, CancellationToken ct)
    {
        var result = new List<DependentRef>();
        var refCols = ModuleRelations.ReferenceColumns.GetValueOrDefault(m.Key) ?? [];
        foreach (var table in await schemas.AllAsync(ct))
        {
            foreach (var col in refCols.Where(rc => table.Find(rc) is not null))
            {
                if (table.Table.Equals(m.Table, StringComparison.OrdinalIgnoreCase) && col != "parent_project_id") continue;
                var n = await c.ExecuteScalarAsync<long>(new CommandDefinition(
                    $"SELECT COUNT(*) FROM {Q(table.Table)} WHERE {Q(col)} = @id", new { id }, tx, cancellationToken: ct));
                if (n == 0) continue;
                var owner = ModuleRegistry.FindByTable(table.Table);
                result.Add(new DependentRef(table.Table, col, n, owner?.Key, owner?.Title ?? (m.Items?.Table == table.Table ? m.Items.Label : null)));
            }
        }
        if (m.Items is { } it && !result.Any(r => r.Table == it.Table))
        {
            var n = await c.ExecuteScalarAsync<long>(new CommandDefinition(
                $"SELECT COUNT(*) FROM {Q(it.Table)} WHERE {Q(it.ForeignKey)} = @id", new { id }, tx, cancellationToken: ct));
            if (n > 0) result.Add(new DependentRef(it.Table, it.ForeignKey, n, null, it.Label));
        }
        if (m.Files.Length > 0)
        {
            var n = await c.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT COUNT(*) FROM file_path WHERE table_name IN @tags AND table_id = @id",
                new { tags = m.Files.Select(f => f.Tag).ToArray(), id }, tx, cancellationToken: ct));
            if (n > 0) result.Add(new DependentRef("file_path", "table_id", n, null, "附件"));
        }
        return result;
    }

    // ---------- 转交 ----------

    public async Task HandoverAsync(string key, long id, string? statusTo, string? personTo, string? comment, ICurrentUser user, CancellationToken ct)
    {
        var m = await records.AuthorizeAsync(key, user, "workflow", ct);
        if (!m.Workflow || m.StatusColumn is null) throw AppError.Validation("该模块没有转交流程");
        statusTo = (statusTo ?? "").Trim();
        personTo = (personTo ?? "").Trim();
        comment = (comment ?? "").Trim();
        var errors = new Dictionary<string, string>();
        if (statusTo.Length == 0 || statusTo == "请选择") errors["statusTo"] = "必填项";   // 请选择 is the lookup placeholder
        if (personTo.Length == 0) errors["personTo"] = "必填项";
        if (comment.Length == 0) errors["comment"] = "必填项";      // legacy submit_history requires all three
        if (errors.Count > 0) throw AppError.Validation("请填写转交信息", errors);
        if (LegacyText.HasSupplementaryChars(comment) && !_compat.Utf8mb4) throw AppError.Validation("备注不支持表情符号或生僻字");

        if (ModuleFields.StatusLookups.GetValueOrDefault(m.Table) is { } statusCategory)
        {
            var allowed = (await lookups.ItemsAsync(statusCategory, ct)).Select(i => i.Item).ToHashSet();
            if (!allowed.Contains(statusTo)) throw AppError.Validation($"状态「{statusTo}」不在「{statusCategory}」选项中");
        }
        if (!(await lookups.ActiveStaffAsync(ct)).Contains(personTo)) throw AppError.Validation($"「{personTo}」不在公司员工名单中");

        await using var c = await db.LegacyAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        var statusFrom = await c.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            $"SELECT {Q(m.StatusColumn)} FROM {Q(m.Table)} WHERE `id` = @id FOR UPDATE", new { id }, tx, cancellationToken: ct));
        if (!await ExistsAsync(c, tx, m.Table, id, ct)) throw AppError.NotFound();

        var history = new Dictionary<string, object?>
        {
            ["table_name"] = m.Table, ["table_id"] = id,
            ["person_from"] = LegacyText.EncodeCompat(user.DisplayName), ["person_to"] = LegacyText.EncodeCompat(personTo),
            ["status_from"] = statusFrom ?? "", ["status_to"] = LegacyText.EncodeCompat(statusTo), ["comment"] = LegacyText.EncodeCompat(comment),
        };
        var hid = await c.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO historys (table_name, table_id, person_from, person_to, status_from, status_to, comment)
            VALUES (@table_name, @table_id, @person_from, @person_to, @status_from, @status_to, @comment);
            SELECT LAST_INSERT_ID();
            """, history, tx, cancellationToken: ct));
        await LegacyAuditWriter.WriteAsync(c, tx, "historys", hid, LegacyAuditWriter.Render("INSERT", "historys", history), user, LegacyAuditWriter.Added, ct);

        var status = LegacyText.EncodeCompat(statusTo);
        await c.ExecuteAsync(new CommandDefinition($"UPDATE {Q(m.Table)} SET {Q(m.StatusColumn)} = @status WHERE `id` = @id",
            new { status, id }, tx, cancellationToken: ct));
        await LegacyAuditWriter.WriteAsync(c, tx, m.Table, id,
            LegacyAuditWriter.Render("UPDATE", m.Table, [new(m.StatusColumn, status)], $"id = {id}"), user, LegacyAuditWriter.Updated, ct);
        await tx.CommitAsync(ct);
    }

    // ---------- related records (REQ-EDIT-4) ----------

    public async Task<RelatedResult> RelatedAsync(string key, long id, ICurrentUser user, CancellationToken ct)
    {
        var m = await records.AuthorizeAsync(key, user, "read", ct);
        var detail = await records.GetAsync(key, id, user, ct);
        var parents = new List<ParentRef>();
        foreach (var r in ModuleRelations.Refs.GetValueOrDefault(m.Key) ?? [])
        {
            if (detail.Record.GetValueOrDefault(r.Column) is not { } v || !long.TryParse(v.ToString(), out var pid) || pid <= 0) continue;
            var target = ModuleRegistry.Get(r.Module);
            if (!user.Has(PermissionCatalog.Code(target.Key, "read"))) continue;
            var titles = await records.TitlesAsync(target, [pid], ct);
            parents.Add(new ParentRef(r.Column, r.Label, target.Key, target.Title, pid, titles.GetValueOrDefault(pid, $"#{pid}（不存在）")));
        }

        var children = new List<RelatedGroup>();
        await using var c = await db.LegacyAsync(ct);
        foreach (var (childKey, r) in ModuleRelations.ReferencedBy(m.Key))
        {
            var child = ModuleRegistry.Get(childKey);
            if (!user.Has(PermissionCatalog.Code(child.Key, "read"))) continue;
            var schema = await schemas.GetAsync(child.Table, ct);
            if (schema.Find(r.Column) is null) continue;
            var cols = new[] { "id" }.Concat(child.TitleColumns).Concat(child.StatusColumn is { } s ? [s] : [])
                .Where(x => schema.Find(x) is not null).Distinct().Select(Q);
            var total = await c.ExecuteScalarAsync<long>(new CommandDefinition(
                $"SELECT COUNT(*) FROM {Q(child.Table)} WHERE {Q(r.Column)} = @id", new { id }, cancellationToken: ct));
            if (total == 0) continue;
            var rows = (await c.QueryAsync(new CommandDefinition(
                    $"SELECT {string.Join(", ", cols)} FROM {Q(child.Table)} WHERE {Q(r.Column)} = @id ORDER BY `id` DESC LIMIT 50",
                    new { id }, cancellationToken: ct)))
                .Select(row => RowMapper.Map((object)row, schema))
                .Select(d => new RelatedRow(Convert.ToInt64(d["id"]), RowMapper.Title(d, child.TitleColumns, d["id"]),
                    child.StatusColumn is { } sc ? d.GetValueOrDefault(sc)?.ToString() : null))
                .ToList();
            children.Add(new RelatedGroup(child.Key, child.Title, r.Column, r.Label, total, rows));
        }
        return new RelatedResult(parents, children);
    }

    // ---------- helpers ----------

    private static object EmptyFor(ColumnInfo col) => col.Kind switch
    {
        ColumnKind.Date or ColumnKind.DateTime => WriteRules.ZeroDate,
        ColumnKind.Money => 0d,
        ColumnKind.Int or ColumnKind.Id => 0,
        _ => "",
    };

    private static async Task<bool> ExistsAsync(MySqlConnection c, MySqlTransaction? tx, string table, long id, CancellationToken ct, bool forUpdate = false) =>
        await c.ExecuteScalarAsync<long?>(new CommandDefinition(
            $"SELECT `id` FROM {Q(table)} WHERE `id` = @id{(forUpdate ? " FOR UPDATE" : "")}", new { id }, tx, cancellationToken: ct)) is not null;

    private async Task<IReadOnlyList<Dictionary<string, object?>>> ItemsAsync(MySqlConnection c, MySqlTransaction tx, ModuleDefinition m, long id, CancellationToken ct)
    {
        if (m.Items is not { } it) return [];
        var itemSchema = await schemas.GetAsync(it.Table, ct);
        return (await c.QueryAsync(new CommandDefinition(
                $"SELECT * FROM {Q(it.Table)} WHERE {Q(it.ForeignKey)} = @id ORDER BY `id`", new { id }, tx, cancellationToken: ct)))
            .Select(r => RowMapper.Map((object)r, itemSchema)).ToList();
    }

    private static string Q(string identifier) => ListQueryBuilder.Q(identifier);
}
