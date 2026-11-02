using Dapper;
using Goldata.Application.Records;
using Goldata.Domain;

namespace Goldata.Infrastructure.Data;

/// <summary>
/// Column metadata of the legacy tables, read once from <c>information_schema</c>.
/// Labels are the Chinese column COMMENTs, with fallbacks for the uncommented standard columns.
/// </summary>
public sealed class SchemaCache(Db db)
{
    private static readonly Dictionary<string, string> FallbackLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["id"] = "编号", ["status"] = "状态", ["comment"] = "备注", ["user_name"] = "录入员", ["timestamp"] = "创建时间",
        ["update_date"] = "滞留日期", ["holder"] = "滞留人员", ["version"] = "版本", ["type"] = "类型", ["tag"] = "编号/标签",
        ["name"] = "姓名", ["bonus"] = "奖金", ["client_representative"] = "甲方对接人", ["complete_date"] = "完成日期",
        ["start_date"] = "开始日期", ["settlement_type"] = "结算类型", ["supplier_id"] = "供应商编号",
        ["project_id"] = "工装项目编号", ["project_retail_id"] = "家装项目编号", ["contract_id"] = "合同编号",
        ["receivable_id"] = "收款编号", ["product_id"] = "产品编号", ["destination_id"] = "目的地", ["delivery_id"] = "出库单编号",
        ["parent_project_id"] = "上级项目", ["time"] = "时间", ["serie"] = "系列", ["employee_id"] = "员工编号",
    };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, TableSchema>? _tables;

    public async Task<TableSchema> GetAsync(string table, CancellationToken ct = default)
    {
        var tables = _tables ?? await LoadAsync(ct);
        return tables.TryGetValue(table, out var t) ? t : throw new AppError(500, "SCHEMA", $"数据表 {table} 不存在");
    }

    private async Task<Dictionary<string, TableSchema>> LoadAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_tables is not null) return _tables;
            await using var c = await db.LegacyAsync(ct);
            var rows = await c.QueryAsync<(string Table, string Column, string DataType, string Comment)>(new CommandDefinition("""
                SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, COLUMN_COMMENT
                FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                ORDER BY TABLE_NAME, ORDINAL_POSITION
                """, cancellationToken: ct));
            _tables = rows.GroupBy(r => r.Table, StringComparer.OrdinalIgnoreCase).ToDictionary(
                g => g.Key,
                g => new TableSchema(g.Key, g.Select(r => new ColumnInfo(
                    r.Column, r.DataType, Label(r.Column, r.Comment), ListQueryBuilder.KindOf(r.Column, r.DataType))).ToList()),
                StringComparer.OrdinalIgnoreCase);
            return _tables;
        }
        finally { _lock.Release(); }
    }

    private static string Label(string column, string? comment) =>
        !string.IsNullOrWhiteSpace(comment) ? comment.Trim() : FallbackLabels.GetValueOrDefault(column, column);
}
