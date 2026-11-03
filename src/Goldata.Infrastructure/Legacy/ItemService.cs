using System.Globalization;
using Dapper;
using Goldata.Application.Items;
using Goldata.Application.Modules;
using Goldata.Application.Records;
using Goldata.Application.Security;
using Goldata.Domain;
using Goldata.Infrastructure.Data;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Goldata.Infrastructure.Legacy;

/// <summary>One submitted grid row: <c>Id</c> null = new row.</summary>
public sealed record ItemInput(long? Id, Dictionary<string, string?> Values);

/// <summary>
/// Line-item grids (REQ-EDIT-3) and item conversions (REQ-LINK-3), written like the legacy grids:
/// rows removed explicitly (never inferred), <c>sub_total = qty × price</c> on the server, header total = Σ sub_total
/// where legacy does it, item text stored raw (legacy doesn't encode grid text), one transaction, audited.
/// </summary>
public sealed class ItemService(Db db, SchemaCache schemas, RecordService records, CatalogueService catalogue, LookupService lookups,
    IOptions<LegacyCompatOptions> compat)
{
    private readonly bool _utf8mb4 = compat.Value.Utf8mb4;

    // ---------- grid save ----------

    public async Task<SaveResult> SaveAsync(string key, long id, IReadOnlyList<ItemInput> items, IReadOnlyList<long> deletedIds, string? ifMatch,
        ICurrentUser user, CancellationToken ct)
    {
        var m = await records.AuthorizeAsync(key, user, "update", ct);
        var spec = ItemSpecs.For(m.Key) ?? throw AppError.Validation("该模块的明细暂不支持在新系统中编辑");
        if (string.IsNullOrEmpty(ifMatch)) throw new AppError(428, "IF_MATCH_REQUIRED", "缺少版本信息（If-Match），请刷新页面后重试");
        var itemSchema = await schemas.GetAsync(spec.Table, ct);
        var headerSchema = await schemas.GetAsync(m.Table, ct);
        var destinations = await DestinationIdsAsync(spec, ct);

        // Validate everything before touching the DB.
        var normalized = new List<(long? Id, Dictionary<string, object?> Values)>();
        var errors = new Dictionary<string, string>();
        for (var i = 0; i < items.Count; i++)
        {
            try { normalized.Add((items[i].Id, await NormalizeAsync(spec, itemSchema, items[i].Values, destinations, ct))); }
            catch (FormatException e) { errors[$"row{i + 1}"] = $"第 {i + 1} 行：{e.Message}"; }
        }
        if (errors.Count > 0) throw AppError.Validation("明细有错误，请检查", errors);

        await using var c = await db.LegacyAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        var header = (IDictionary<string, object?>?)await c.QuerySingleOrDefaultAsync(new CommandDefinition(
            $"SELECT * FROM {Q(m.Table)} WHERE `id` = @id FOR UPDATE", new { id }, tx, cancellationToken: ct)) ?? throw AppError.NotFound();
        var existing = (await c.QueryAsync(new CommandDefinition(
                $"SELECT * FROM {Q(spec.Table)} WHERE {Q(spec.ForeignKey)} = @id ORDER BY `id`", new { id }, tx, cancellationToken: ct)))
            .Select(r => (IDictionary<string, object?>)r).ToList();

        // Same canonical ETag as GET (header + items): catches edits in the new app and in PHP.
        var visible = ListQueryBuilder.VisibleColumns(m, headerSchema).Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = RowMapper.Map(header.Where(kv => visible.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value), headerSchema);
        if (RecordService.ETag(current, existing.Select(r => RowMapper.Map(r.ToDictionary(kv => kv.Key, kv => kv.Value), itemSchema)).ToList()) != ifMatch)
            throw AppError.Conflict("CONCURRENT_EDIT", "该记录或明细已被其他人修改，请查看最新内容后再保存");

        var existingById = existing.ToDictionary(r => Convert.ToInt64(r["id"]), r => r);
        foreach (var del in deletedIds.Distinct())
        {
            if (!existingById.Remove(del)) throw AppError.Validation($"明细 #{del} 不属于该记录");
            await c.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q(spec.Table)} WHERE `id` = @del", new { del }, tx, cancellationToken: ct));
            await LegacyAuditWriter.WriteAsync(c, tx, spec.Table, del, LegacyAuditWriter.Render("DELETE", spec.Table, [], $"id = {del}"), user,
                LegacyAuditWriter.Deleted, ct);
        }

        foreach (var (itemId, values) in normalized)
        {
            if (spec.SubTotal is not null) values[spec.SubTotal] = SubTotal(values, spec);
            if (itemId is { } iid)
            {
                if (!existingById.TryGetValue(iid, out var row)) throw AppError.Validation($"明细 #{iid} 不属于该记录或已被删除");
                var changes = WriteRules.Changes(values, row.ToDictionary(kv => kv.Key, kv => kv.Value));
                if (changes.Count == 0) continue;
                await UpdateAsync(c, tx, spec.Table, iid, changes, ct);
                await LegacyAuditWriter.WriteAsync(c, tx, spec.Table, iid, LegacyAuditWriter.Render("UPDATE", spec.Table, changes, $"id = {iid}"), user,
                    LegacyAuditWriter.Updated, ct);
            }
            else
            {
                values[spec.ForeignKey] = id;
                FillRequired(itemSchema, values);
                var newId = await InsertAsync(c, tx, spec.Table, values, ct);
                await LegacyAuditWriter.WriteAsync(c, tx, spec.Table, newId, LegacyAuditWriter.Render("INSERT", spec.Table, values), user,
                    LegacyAuditWriter.Added, ct);
            }
        }

        await RecalcHeaderTotalAsync(c, tx, m, spec, id, user, ct);
        await tx.CommitAsync(ct);
        var detail = await records.GetAsync(key, id, user, ct);
        return new SaveResult(id, detail.ETag);
    }

    // ---------- conversions (REQ-LINK-3) ----------

    /// <summary>采购计划明细 → 订单明细: product, unit, standard, brand, buy_unit_price → unit_price, comment (no qty, as legacy).</summary>
    public Task<int> PlanItemsToOrderAsync(IReadOnlyList<long> itemIds, long orderId, ICurrentUser user, CancellationToken ct) =>
        CopyItemsAsync("buy-plans", "buy_plan_items", itemIds, orderId, user,
            [("product_id", "product_id"), ("unit", "unit"), ("standard", "standard"), ("brand", "brand"), ("buy_unit_price", "unit_price"),
             ("item_comment", "item_comment")], ct);

    /// <summary>请购明细 → 订单明细. Default: due date, priority, ETA; "report" variant (请购报表): suggested unit price.</summary>
    public Task<int> RequestItemsToOrderAsync(IReadOnlyList<long> itemIds, long orderId, bool reportVariant, ICurrentUser user, CancellationToken ct) =>
        CopyItemsAsync("buy-requests", "buy_request_items", itemIds, orderId, user, reportVariant
            ? [("product_id", "product_id"), ("unit", "unit"), ("standard", "standard"), ("brand", "brand"), ("unit_price", "unit_price")]
            : [("product_id", "product_id"), ("unit", "unit"), ("standard", "standard"), ("brand", "brand"), ("request_due_date", "request_due_date"),
               ("priority", "priority"), ("eta", "eta"), ("item_comment", "item_comment")], ct);

    private async Task<int> CopyItemsAsync(string sourceModule, string sourceTable, IReadOnlyList<long> itemIds, long orderId, ICurrentUser user,
        (string From, string To)[] map, CancellationToken ct)
    {
        await records.AuthorizeAsync(sourceModule, user, "read", ct);
        var order = await records.AuthorizeAsync("buy-orders", user, "update", ct);
        if (itemIds.Count == 0) throw AppError.Validation("请先选择明细");
        var target = ItemSpecs.For("buy-orders")!;
        var targetSchema = await schemas.GetAsync(target.Table, ct);
        var sourceSchema = await schemas.GetAsync(sourceTable, ct);

        await using var c = await db.LegacyAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        if (await c.ExecuteScalarAsync<long?>(new CommandDefinition($"SELECT CAST(id AS SIGNED) FROM {Q(order.Table)} WHERE id = @orderId FOR UPDATE",
                new { orderId }, tx, cancellationToken: ct)) is null) throw AppError.NotFound("订单");
        var cols = string.Join(", ", map.Where(x => sourceSchema.Find(x.From) is not null).Select(x => Q(x.From)));
        var rows = (await c.QueryAsync(new CommandDefinition($"SELECT `id`, {cols} FROM {Q(sourceTable)} WHERE `id` IN @itemIds ORDER BY `id`",
            new { itemIds }, tx, cancellationToken: ct))).Select(r => (IDictionary<string, object?>)r).ToList();
        if (rows.Count != itemIds.Distinct().Count()) throw AppError.Validation("部分明细不存在");

        foreach (var row in rows)
        {
            var values = map.Where(x => row.ContainsKey(x.From) && targetSchema.Find(x.To) is not null)
                .ToDictionary(x => x.To, x => Clean(row[x.From]));
            values[target.ForeignKey] = orderId;
            FillRequired(targetSchema, values);
            var newId = await InsertAsync(c, tx, target.Table, values, ct);
            await LegacyAuditWriter.WriteAsync(c, tx, target.Table, newId,
                LegacyAuditWriter.Render("INSERT", target.Table, values) + $" /* from {sourceTable} #{row["id"]} */", user, LegacyAuditWriter.Added, ct);
        }
        await tx.CommitAsync(ct);
        return rows.Count;
    }

    /// <summary>
    /// 订单 → 收货单 (insert.php caller buy_orders): create the receipt, link it (buy_receipt_order, share_amount 0 as legacy) and copy
    /// every order line (product, unit, standard, brand, unit price, qty, sub_total, comment); the receipt amount = Σ sub_total.
    /// </summary>
    public async Task<long> OrderToReceiptAsync(long orderId, IReadOnlyDictionary<string, string?> headerValues, ICurrentUser user, CancellationToken ct)
    {
        await records.AuthorizeAsync("buy-orders", user, "read", ct);
        var receipt = await records.AuthorizeAsync("buy-receipts", user, "create", ct);
        var receiptSchema = await schemas.GetAsync(receipt.Table, ct);
        var spec = ItemSpecs.For("buy-receipts")!;
        var itemSchema = await schemas.GetAsync(spec.Table, ct);
        var values = WriteRules.Normalize(receipt, receiptSchema, headerValues, isCreate: true, _utf8mb4);
        if (receiptSchema.Find("user_name") is not null && string.IsNullOrEmpty(values.GetValueOrDefault("user_name") as string))
            values["user_name"] = LegacyText.EncodeCompat(user.DisplayName);

        await using var c = await db.LegacyAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        var o = (IDictionary<string, object?>?)await c.QuerySingleOrDefaultAsync(new CommandDefinition(
            "SELECT id, supplier FROM buy_orders WHERE id = @orderId FOR UPDATE", new { orderId }, tx, cancellationToken: ct)) ?? throw AppError.NotFound("订单");
        if (receiptSchema.Find("supplier") is not null && !values.ContainsKey("supplier")) values["supplier"] = o["supplier"] ?? "";
        FillRequired(receiptSchema, values);
        var receiptId = await InsertAsync(c, tx, receipt.Table, values, ct);
        await LegacyAuditWriter.WriteAsync(c, tx, receipt.Table, receiptId, LegacyAuditWriter.Render("INSERT", receipt.Table, values), user, LegacyAuditWriter.Added, ct);

        var link = new Dictionary<string, object?> { ["buy_receipt_id"] = receiptId, ["buy_order_id"] = orderId, ["share_amount"] = 0d };
        var linkId = await InsertAsync(c, tx, "buy_receipt_order", link, ct);
        await LegacyAuditWriter.WriteAsync(c, tx, "buy_receipt_order", linkId, LegacyAuditWriter.Render("INSERT", "buy_receipt_order", link), user, LegacyAuditWriter.Added, ct);

        var lines = (await c.QueryAsync(new CommandDefinition("""
            SELECT product_id, unit, standard, brand, unit_price, qty, sub_total, item_comment FROM buy_order_items WHERE buy_order_id = @orderId ORDER BY id
            """, new { orderId }, tx, cancellationToken: ct))).Select(r => (IDictionary<string, object?>)r).ToList();
        foreach (var line in lines)
        {
            var item = line.ToDictionary(kv => kv.Key, kv => Clean(kv.Value));
            item[spec.ForeignKey] = receiptId;
            FillRequired(itemSchema, item);
            var itemId = await InsertAsync(c, tx, spec.Table, item, ct);
            await LegacyAuditWriter.WriteAsync(c, tx, spec.Table, itemId, LegacyAuditWriter.Render("INSERT", spec.Table, item), user, LegacyAuditWriter.Added, ct);
        }
        await RecalcHeaderTotalAsync(c, tx, receipt, spec, receiptId, user, ct);
        await tx.CommitAsync(ct);
        return receiptId;
    }

    // ---------- helpers ----------

    private async Task<Dictionary<string, object?>> NormalizeAsync(ItemTableSpec spec, TableSchema schema, IReadOnlyDictionary<string, string?> input,
        IReadOnlySet<long> destinations, CancellationToken ct)
    {
        var allowed = spec.Fields.ToDictionary(f => f.Column, StringComparer.OrdinalIgnoreCase);
        foreach (var k in input.Keys)
            if (!allowed.ContainsKey(k)) throw new FormatException($"字段不可编辑：{k}");

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var f in spec.Fields)
        {
            if (schema.Find(f.Column) is not { } col) continue;
            var text = (input.GetValueOrDefault(f.Column) ?? "").Trim();
            values[col.Name] = f.Kind switch
            {
                ItemFieldKind.Product => await ProductAsync(text, ct),
                ItemFieldKind.Destination => Destination(text, destinations),
                ItemFieldKind.Number or ItemFieldKind.Money => Number(text, col.Label),
                ItemFieldKind.Int => text.Length == 0 ? 0 : long.TryParse(text, out var n) ? n : throw new FormatException($"{col.Label}请输入整数"),
                ItemFieldKind.Date => text.Length == 0 ? WriteRules.ZeroDate
                    : DateOnly.TryParse(text, CultureInfo.InvariantCulture, out var d) ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : throw new FormatException($"{col.Label}日期格式应为 YYYY-MM-DD"),
                _ => !_utf8mb4 && LegacyText.HasSupplementaryChars(text) ? throw new FormatException($"{col.Label}不支持表情符号或生僻字") : text,
            };
        }
        // Unit auto-fill from the product, as the legacy dropdown does.
        if (values.TryGetValue("product_id", out var pid) && pid is long p && values.TryGetValue("unit", out var u) && string.IsNullOrEmpty(u as string))
            values["unit"] = (await catalogue.GetAsync(p, ct))?.Unit ?? "";
        return values;
    }

    private async Task<object?> ProductAsync(string text, CancellationToken ct)
    {
        if (text.Length == 0) throw new FormatException("请选择产品");
        if (!long.TryParse(text, out var id) || await catalogue.GetAsync(id, ct) is null) throw new FormatException("产品不在产品分类表中");
        return id;
    }

    private static object Destination(string text, IReadOnlySet<long> destinations)
    {
        if (text.Length == 0) throw new FormatException("请选择进库目的地");
        if (!long.TryParse(text, out var id) || !destinations.Contains(id)) throw new FormatException("进库目的地无效");
        return id;
    }

    private static double Number(string text, string label)
    {
        if (text.Length == 0) return 0;
        if (!decimal.TryParse(text.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) throw new FormatException($"{label}请输入数字");
        return (double)Math.Round(d, 4);
    }

    private static double SubTotal(IReadOnlyDictionary<string, object?> v, ItemTableSpec spec) =>
        Math.Round(Convert.ToDouble(v.GetValueOrDefault(spec.Qty!) ?? 0d) * Convert.ToDouble(v.GetValueOrDefault(spec.Price!) ?? 0d), 4);

    private async Task<IReadOnlySet<long>> DestinationIdsAsync(ItemTableSpec spec, CancellationToken ct) =>
        spec.Fields.Any(f => f.Kind == ItemFieldKind.Destination)
            ? (await lookups.ItemsAsync(ItemSpecs.DestinationCategory, ct)).Select(i => (long)i.Id).ToHashSet()
            : new HashSet<long>();

    private static async Task RecalcHeaderTotalAsync(MySqlConnection c, MySqlTransaction tx, ModuleDefinition m, ItemTableSpec spec, long id,
        ICurrentUser user, CancellationToken ct)
    {
        if (spec.HeaderTotal is null || spec.SubTotal is null) return;
        var sum = Math.Round(await c.ExecuteScalarAsync<double>(new CommandDefinition(
            $"SELECT COALESCE(SUM({Q(spec.SubTotal)}), 0) FROM {Q(spec.Table)} WHERE {Q(spec.ForeignKey)} = @id", new { id }, tx, cancellationToken: ct)), 4);
        var current = await c.ExecuteScalarAsync<double?>(new CommandDefinition(
            $"SELECT {Q(spec.HeaderTotal)} FROM {Q(m.Table)} WHERE `id` = @id", new { id }, tx, cancellationToken: ct)) ?? 0;
        if (Math.Abs(current - sum) < 0.00005) return;
        await c.ExecuteAsync(new CommandDefinition($"UPDATE {Q(m.Table)} SET {Q(spec.HeaderTotal)} = @sum WHERE `id` = @id", new { sum, id }, tx, cancellationToken: ct));
        await LegacyAuditWriter.WriteAsync(c, tx, m.Table, id, LegacyAuditWriter.Render("UPDATE", m.Table, [new(spec.HeaderTotal, sum)], $"id = {id}"), user,
            LegacyAuditWriter.Updated, ct);
    }

    /// <summary>Strict mode: NOT NULL columns without a default get the legacy "empty" value.</summary>
    private static void FillRequired(TableSchema schema, Dictionary<string, object?> values)
    {
        foreach (var col in schema.Columns.Where(x => !x.Nullable && !x.HasDefault && x.Name != "id" && !values.ContainsKey(x.Name)))
            values[col.Name] = col.Kind switch
            {
                ColumnKind.Date or ColumnKind.DateTime => WriteRules.ZeroDate,
                ColumnKind.Money => 0d,
                ColumnKind.Int or ColumnKind.Id => 0,
                _ => "",
            };
    }

    private static object? Clean(object? v) => v switch
    {
        DBNull => null,
        DateTime d when d == DateTime.MinValue || d.Year < 1000 => WriteRules.ZeroDate,
        DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => v,
    };

    private static async Task<long> InsertAsync(MySqlConnection c, MySqlTransaction tx, string table, IReadOnlyDictionary<string, object?> values, CancellationToken ct)
    {
        var cols = values.Keys.ToList();
        var p = new DynamicParameters();
        for (var i = 0; i < cols.Count; i++) p.Add("v" + i, values[cols[i]]);
        return await c.ExecuteScalarAsync<long>(new CommandDefinition(
            $"INSERT INTO {Q(table)} ({string.Join(", ", cols.Select(Q))}) VALUES ({string.Join(", ", cols.Select((_, i) => "@v" + i))}); SELECT LAST_INSERT_ID();",
            p, tx, cancellationToken: ct));
    }

    private static async Task UpdateAsync(MySqlConnection c, MySqlTransaction tx, string table, long id, IReadOnlyDictionary<string, object?> changes, CancellationToken ct)
    {
        var cols = changes.Keys.ToList();
        var p = new DynamicParameters(new { id });
        for (var i = 0; i < cols.Count; i++) p.Add("v" + i, changes[cols[i]]);
        await c.ExecuteAsync(new CommandDefinition(
            $"UPDATE {Q(table)} SET {string.Join(", ", cols.Select((col, i) => $"{Q(col)} = @v{i}"))} WHERE `id` = @id", p, tx, cancellationToken: ct));
    }

    private static string Q(string identifier) => ListQueryBuilder.Q(identifier);
}
