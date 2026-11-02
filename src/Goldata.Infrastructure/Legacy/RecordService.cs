using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Goldata.Application.Modules;
using Goldata.Application.Permissions;
using Goldata.Application.Records;
using Goldata.Application.Security;
using Goldata.Domain;
using Goldata.Infrastructure.Data;
using Goldata.Infrastructure.Settings;

namespace Goldata.Infrastructure.Legacy;

public sealed record ListResult(
    IReadOnlyList<Dictionary<string, object?>> Rows, long Total, IReadOnlyDictionary<string, double?> Totals, int Page, int PageSize);

public sealed record FieldMeta(string Name, string Label, string Kind);

public sealed record ModuleMeta(
    string Key, string Title, string Group, string GroupLabel, string[] TitleColumns, string? StatusColumn, string[] ClosedValues,
    string? YearColumn, string DefaultSort, string[] Totals, bool Workflow, bool HasItems, string? ItemsLabel,
    IReadOnlyList<FileSlot> FileSlots, IReadOnlyList<FieldMeta> Fields, IReadOnlyList<FieldMeta> ItemFields, IReadOnlyList<string> Actions);

public sealed record RecordDetail(
    Dictionary<string, object?> Record, string Title, IReadOnlyList<Dictionary<string, object?>> Items, string ETag);

public sealed record LogEntry(int Id, string User, string Tag, string SqlText, string UpdateTime);

public sealed record HistoryEntry(int Id, string PersonFrom, string PersonTo, string StatusFrom, string StatusTo, string Comment, string Timestamp);

/// <summary>Generic read access to the legacy module tables (REQ-LIST, REQ-EDIT-4, REQ-AUD-3). Writes come per module in P1+.</summary>
public sealed class RecordService(Db db, SchemaCache schemas, SettingsService settings)
{
    /// <summary>Checks the module read permission and the MCP HR rule (REQ-MCP-5).</summary>
    public async Task<ModuleDefinition> AuthorizeAsync(string key, ICurrentUser user, string action = "read", CancellationToken ct = default)
    {
        var m = ModuleRegistry.Get(key);
        user.Require(PermissionCatalog.Code(m.Key, action));
        if (m.IsHr && user.Channel == Channel.Mcp && !await settings.GetBoolAsync(SettingsService.McpHrEnabled, ct))
            throw AppError.Forbidden();
        return m;
    }

    public async Task<IReadOnlyList<ModuleMeta>> MetaAsync(ICurrentUser user, CancellationToken ct)
    {
        var result = new List<ModuleMeta>();
        foreach (var m in ModuleRegistry.All.Where(m => user.Has(PermissionCatalog.Code(m.Key, "read"))))
        {
            var actions = PermissionCatalog.ActionsFor(m).Where(a => user.Has(PermissionCatalog.Code(m.Key, a))).ToList();
            if (!m.Generic)
            {
                result.Add(new(m.Key, m.Title, m.Group.ToString(), MenuGroups.Label(m.Group), [], null, [], null, "", [], false, false,
                    null, [], [], [], actions));
                continue;
            }
            var schema = await schemas.GetAsync(m.Table, ct);
            IReadOnlyList<FieldMeta> itemFields = [];
            if (m.Items is { } it)
                itemFields = (await schemas.GetAsync(it.Table, ct)).Columns.Select(ToMeta).ToList();
            result.Add(new(m.Key, m.Title, m.Group.ToString(), MenuGroups.Label(m.Group), m.TitleColumns, m.StatusColumn, m.ClosedValues,
                m.YearColumn, m.DefaultSort, m.Totals, m.Workflow, m.Items is not null, m.Items?.Label, m.Files,
                ListQueryBuilder.VisibleColumns(m, schema).Select(ToMeta).ToList(), itemFields, actions));
        }
        return result;
    }

    private static FieldMeta ToMeta(ColumnInfo c) => new(c.Name, c.Label, c.Kind.ToString().ToLowerInvariant());

    public async Task<ListResult> ListAsync(string key, ListRequest request, ICurrentUser user, CancellationToken ct)
    {
        var m = await AuthorizeAsync(key, user, ct: ct);
        if (!m.Generic) throw AppError.NotFound("模块");
        var schema = await schemas.GetAsync(m.Table, ct);
        var (page, summary) = ListQueryBuilder.Build(m, schema, request);

        await using var c = await db.LegacyAsync(ct);
        var rows = (await c.QueryAsync(new CommandDefinition(page.Sql, new DynamicParameters(page.Parameters), cancellationToken: ct)))
            .Select(r =>
            {
                var d = RowMapper.Map((object)r, schema);
                d["_title"] = RowMapper.Title(d, m.TitleColumns, d.GetValueOrDefault("id"));
                return d;
            }).ToList();

        var sum = (IDictionary<string, object?>)await c.QuerySingleAsync(new CommandDefinition(
            summary.Sql, new DynamicParameters(summary.Parameters), cancellationToken: ct));
        var totals = sum.Where(kv => kv.Key.StartsWith("sum_", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key[4..], kv => kv.Value is null or DBNull ? null : (double?)Convert.ToDouble(kv.Value));

        return new ListResult(rows, Convert.ToInt64(sum["total"]), totals,
            Math.Max(1, request.Page), Math.Clamp(request.PageSize, 1, ListQueryBuilder.MaxPageSize));
    }

    public async Task<IReadOnlyList<int>> YearsAsync(string key, ICurrentUser user, CancellationToken ct)
    {
        var m = await AuthorizeAsync(key, user, ct: ct);
        if (m.YearColumn is null) return [];
        var schema = await schemas.GetAsync(m.Table, ct);
        if (schema.Find(m.YearColumn) is null) return [];
        await using var c = await db.LegacyAsync(ct);
        var col = ListQueryBuilder.Q(m.YearColumn);
        return (await c.QueryAsync<int>(new CommandDefinition(
            $"SELECT DISTINCT YEAR({col}) AS y FROM {ListQueryBuilder.Q(m.Table)} WHERE YEAR({col}) > 1900 ORDER BY y DESC",
            cancellationToken: ct))).ToList();
    }

    public async Task<RecordDetail> GetAsync(string key, long id, ICurrentUser user, CancellationToken ct)
    {
        var m = await AuthorizeAsync(key, user, ct: ct);
        if (!m.Generic) throw AppError.NotFound("模块");
        var schema = await schemas.GetAsync(m.Table, ct);
        var cols = string.Join(", ", ListQueryBuilder.VisibleColumns(m, schema).Select(x => ListQueryBuilder.Q(x.Name)));

        await using var c = await db.LegacyAsync(ct);
        var row = await c.QuerySingleOrDefaultAsync(new CommandDefinition(
            $"SELECT {cols} FROM {ListQueryBuilder.Q(m.Table)} WHERE `id` = @id", new { id }, cancellationToken: ct))
            ?? throw AppError.NotFound();
        var record = RowMapper.Map((object)row, schema);

        IReadOnlyList<Dictionary<string, object?>> items = [];
        if (m.Items is { } it)
        {
            var itemSchema = await schemas.GetAsync(it.Table, ct);
            items = (await c.QueryAsync(new CommandDefinition(
                    $"SELECT * FROM {ListQueryBuilder.Q(it.Table)} WHERE {ListQueryBuilder.Q(it.ForeignKey)} = @id ORDER BY `id`",
                    new { id }, cancellationToken: ct)))
                .Select(r => RowMapper.Map((object)r, itemSchema)).ToList();
        }

        return new RecordDetail(record, RowMapper.Title(record, m.TitleColumns, id), items, ETag(record, items));
    }

    /// <summary>SHA-256 of the canonical row JSON: <c>timestamp</c> is a create time, so it can't be a row version (002-spec §3.4).</summary>
    public static string ETag(IReadOnlyDictionary<string, object?> record, IReadOnlyList<Dictionary<string, object?>> items)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            r = record.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList(),
            i = items.Select(x => x.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList()).ToList(),
        });
        return "\"" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..32] + "\"";
    }

    public async Task<IReadOnlyList<LogEntry>> LogsAsync(string key, long id, ICurrentUser user, CancellationToken ct)
    {
        var m = await AuthorizeAsync(key, user, ct: ct);
        await using var c = await db.LegacyAsync(ct);
        var rows = await c.QueryAsync<(int Id, string User, string Tag, string SqlText, DateTime UpdateTime)>(new CommandDefinition("""
            SELECT id, user, tag, sql_text, update_time FROM update_logs
            WHERE table_name = @table AND row_id = @id ORDER BY update_time DESC, id DESC LIMIT 500
            """, new { table = m.Table, id }, cancellationToken: ct));
        return rows.Select(r => new LogEntry(r.Id, LegacyText.Decode(r.User), r.Tag.Trim(), LegacyText.Decode(r.SqlText),
            r.UpdateTime.ToString("yyyy-MM-dd HH:mm:ss"))).ToList();
    }

    public async Task<IReadOnlyList<HistoryEntry>> HistoryAsync(string key, long id, ICurrentUser user, CancellationToken ct)
    {
        var m = await AuthorizeAsync(key, user, ct: ct);
        await using var c = await db.LegacyAsync(ct);
        var rows = await c.QueryAsync<(int Id, string PersonFrom, string PersonTo, string StatusFrom, string StatusTo, string Comment, DateTime Timestamp)>(
            new CommandDefinition("""
                SELECT id, person_from, person_to, status_from, status_to, comment, timestamp FROM historys
                WHERE table_name = @table AND table_id = @id ORDER BY id DESC
                """, new { table = m.Table, id }, cancellationToken: ct));
        return rows.Select(r => new HistoryEntry(r.Id, LegacyText.Decode(r.PersonFrom), LegacyText.Decode(r.PersonTo),
            LegacyText.Decode(r.StatusFrom), LegacyText.Decode(r.StatusTo), LegacyText.Decode(r.Comment),
            r.Timestamp.ToString("yyyy-MM-dd HH:mm"))).ToList();
    }

    /// <summary>Title lookup for many ids of one module in one query (to-dos, search; no N+1).</summary>
    public async Task<Dictionary<long, string>> TitlesAsync(ModuleDefinition m, IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        if (ids.Count == 0 || !m.Generic) return [];
        var schema = await schemas.GetAsync(m.Table, ct);
        var cols = m.TitleColumns.Where(x => schema.Find(x) is not null).ToList();
        var select = string.Join(", ", new[] { "id" }.Concat(cols).Select(ListQueryBuilder.Q));
        await using var c = await db.LegacyAsync(ct);
        var rows = await c.QueryAsync(new CommandDefinition(
            $"SELECT {select} FROM {ListQueryBuilder.Q(m.Table)} WHERE `id` IN @ids", new { ids }, cancellationToken: ct));
        return rows.Select(r => RowMapper.Map((object)r, schema))
            .ToDictionary(d => Convert.ToInt64(d["id"]), d => RowMapper.Title(d, cols, d["id"]));
    }

    /// <summary>Cross-module search for the global search box and MCP <c>search_records</c>.</summary>
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, IReadOnlyCollection<string>? modules, int limitPerModule,
        ICurrentUser user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var hits = new List<SearchHit>();
        foreach (var m in ModuleRegistry.All.Where(m => m.Generic && (modules is null || modules.Count == 0 || modules.Contains(m.Key))))
        {
            try { await AuthorizeAsync(m.Key, user, ct: ct); }
            catch (AppError) { continue; }
            var result = await ListAsync(m.Key, new ListRequest { Q = query, IncludeClosed = true, PageSize = limitPerModule }, user, ct);
            hits.AddRange(result.Rows.Select(r => new SearchHit(m.Key, m.Title, Convert.ToInt64(r["id"]), (string)r["_title"]!,
                m.StatusColumn is { } sc ? r.GetValueOrDefault(sc)?.ToString() : null)));
        }
        return hits;
    }
}

public sealed record SearchHit(string Module, string ModuleTitle, long Id, string Title, string? Status);
