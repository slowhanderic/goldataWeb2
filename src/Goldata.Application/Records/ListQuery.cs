using System.Text;
using Goldata.Application.Modules;
using Goldata.Domain;

namespace Goldata.Application.Records;

public enum ColumnKind { Id, Int, Text, Date, DateTime, Money, Json }

/// <summary>A column of a legacy table, read from <c>information_schema</c> (label = Chinese COMMENT).</summary>
public sealed record ColumnInfo(string Name, string DataType, string Label, ColumnKind Kind);

public sealed record TableSchema(string Table, IReadOnlyList<ColumnInfo> Columns)
{
    private readonly Dictionary<string, ColumnInfo> _byName =
        Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
    public ColumnInfo? Find(string name) => _byName.GetValueOrDefault(name);
}

public sealed record ColumnFilter(string Column, string Value, bool Not = false);
public sealed record DateRangeFilter(string Column, DateOnly? From, DateOnly? To);

/// <summary>Query-string model for <c>GET /api/v1/{module}</c> (REQ-LIST-1…6).</summary>
public sealed record ListRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 50;
    public string? Sort { get; init; }
    public string? Q { get; init; }
    public int? Year { get; init; }
    public bool IncludeClosed { get; init; }
    public IReadOnlyList<ColumnFilter> Filters { get; init; } = [];
    public IReadOnlyList<DateRangeFilter> DateRanges { get; init; } = [];
}

public sealed record SqlCommand(string Sql, IReadOnlyDictionary<string, object?> Parameters);

/// <summary>
/// Builds parameterised SQL for module lists. Replaces <c>conditions_to_sql()</c>/<c>orderby_to_sql()</c>:
/// column names come only from the live schema whitelist, and every value is a parameter (NFR-SEC-1).
/// </summary>
public static class ListQueryBuilder
{
    public const int MaxPageSize = 500;

    public static ColumnKind KindOf(string column, string dataType) => dataType.ToLowerInvariant() switch
    {
        _ when column == "id" => ColumnKind.Id,
        "int" or "bigint" or "smallint" or "tinyint" => ColumnKind.Int,
        "double" or "float" or "decimal" => ColumnKind.Money,
        "date" => ColumnKind.Date,
        "datetime" or "timestamp" => ColumnKind.DateTime,
        "longtext" when column == "postArray" => ColumnKind.Json,
        _ => ColumnKind.Text,
    };

    public static IReadOnlyList<ColumnInfo> VisibleColumns(ModuleDefinition m, TableSchema schema) =>
        schema.Columns.Where(c => !m.HiddenColumns.Contains(c.Name, StringComparer.OrdinalIgnoreCase)).ToList();

    /// <summary>Returns (page query, count + totals query).</summary>
    public static (SqlCommand Page, SqlCommand Summary) Build(ModuleDefinition m, TableSchema schema, ListRequest r)
    {
        var p = new Dictionary<string, object?>();
        var where = BuildWhere(m, schema, r, p);

        var cols = string.Join(", ", VisibleColumns(m, schema).Select(c => Q(c.Name)));
        var order = BuildOrder(m, schema, r.Sort);
        var pageSize = Math.Clamp(r.PageSize, 1, MaxPageSize);
        var page = Math.Max(1, r.Page);
        var pageParams = new Dictionary<string, object?>(p) { ["limit"] = pageSize, ["offset"] = (page - 1) * pageSize };

        var pageSql = $"SELECT {cols} FROM {Q(m.Table)}{where} ORDER BY {order} LIMIT @limit OFFSET @offset";

        var totals = m.Totals.Where(t => schema.Find(t) is { Kind: ColumnKind.Money })
            .Select(t => $", ROUND(SUM({Q(t)}), 2) AS {Q("sum_" + t)}");
        var summarySql = $"SELECT COUNT(*) AS total{string.Concat(totals)} FROM {Q(m.Table)}{where}";

        return (new SqlCommand(pageSql, pageParams), new SqlCommand(summarySql, p));
    }

    public static string BuildWhere(ModuleDefinition m, TableSchema schema, ListRequest r, Dictionary<string, object?> p)
    {
        var parts = new List<string>();
        var n = 0;
        string Param(object? v) { var name = "p" + n++; p[name] = v; return "@" + name; }

        // Default view hides closed/void records. Legacy uses `col NOT LIKE '%v%'`; NULL statuses stay visible here.
        if (!r.IncludeClosed && m.StatusColumn is { } sc && schema.Find(sc) is not null)
            foreach (var v in m.ClosedValues)
                parts.Add($"COALESCE({Q(sc)}, '') NOT LIKE {Param(LegacyText.ContainsPattern(v))}");

        if (r.Year is { } year && m.YearColumn is { } yc && schema.Find(yc) is not null)
            parts.Add($"YEAR({Q(yc)}) = {Param(year)}");

        foreach (var f in r.Filters)
        {
            var c = schema.Find(f.Column) ?? throw AppError.Validation($"未知字段：{f.Column}");
            if (string.IsNullOrWhiteSpace(f.Value)) continue;
            if (c.Kind is ColumnKind.Id or ColumnKind.Int)
            {
                if (!long.TryParse(f.Value, out var num)) throw AppError.Validation($"字段 {c.Label} 需要数字");
                parts.Add($"{Q(c.Name)} {(f.Not ? "<>" : "=")} {Param(num)}");
            }
            else
            {
                parts.Add($"COALESCE({Q(c.Name)}, '') {(f.Not ? "NOT LIKE" : "LIKE")} {Param(LegacyText.ContainsPattern(f.Value))}");
            }
        }

        foreach (var d in r.DateRanges)
        {
            var c = schema.Find(d.Column) ?? throw AppError.Validation($"未知字段：{d.Column}");
            if (c.Kind is not (ColumnKind.Date or ColumnKind.DateTime)) throw AppError.Validation($"字段 {c.Label} 不是日期");
            if (d.From is { } from) parts.Add($"CAST({Q(c.Name)} AS DATE) >= {Param(from.ToString("yyyy-MM-dd"))}");
            if (d.To is { } to) parts.Add($"CAST({Q(c.Name)} AS DATE) <= {Param(to.ToString("yyyy-MM-dd"))}");
        }

        if (!string.IsNullOrWhiteSpace(r.Q))
        {
            var searchCols = SearchColumns(m, schema);
            if (searchCols.Count > 0)
            {
                var pattern = Param(LegacyText.ContainsPattern(r.Q));
                var ors = searchCols.Select(c => $"COALESCE({Q(c)}, '') LIKE {pattern}");
                var idClause = long.TryParse(r.Q.Trim(), out var id) ? $" OR {Q("id")} = {Param(id)}" : "";
                parts.Add($"({string.Join(" OR ", ors)}{idClause})");
            }
        }

        return parts.Count == 0 ? "" : " WHERE " + string.Join(" AND ", parts);
    }

    /// <summary>Title columns first, then every other text column (comments, names, numbers).</summary>
    public static List<string> SearchColumns(ModuleDefinition m, TableSchema schema)
    {
        var cols = m.TitleColumns.Where(c => schema.Find(c) is { Kind: ColumnKind.Text }).ToList();
        cols.AddRange(VisibleColumns(m, schema).Where(c => c.Kind == ColumnKind.Text && !cols.Contains(c.Name)).Select(c => c.Name));
        return cols;
    }

    public static string BuildOrder(ModuleDefinition m, TableSchema schema, string? sort)
    {
        var sb = new StringBuilder();
        foreach (var raw in (string.IsNullOrWhiteSpace(sort) ? m.DefaultSort : sort).Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var desc = raw.StartsWith('-');
            var name = raw.TrimStart('-', '+').Trim();
            var c = schema.Find(name);
            if (c is null) continue;   // ignore unknown sort keys rather than failing the list
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(Q(c.Name)).Append(desc ? " DESC" : " ASC");
        }
        if (sb.Length > 0) sb.Append(", ");
        sb.Append("`id` DESC");          // stable paging
        return sb.ToString();
    }

    /// <summary>Quotes an identifier that has already been checked against the schema whitelist.</summary>
    public static string Q(string identifier)
    {
        if (identifier.Length == 0 || identifier.Any(ch => !(char.IsAsciiLetterOrDigit(ch) || ch == '_')))
            throw new ArgumentException($"Invalid identifier '{identifier}'.");
        return "`" + identifier + "`";
    }
}
