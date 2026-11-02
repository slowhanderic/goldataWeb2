using Dapper;
using Goldata.Application.Modules;
using Goldata.Application.Permissions;
using Goldata.Application.Security;
using Goldata.Domain;
using Goldata.Infrastructure.Data;
using Goldata.Infrastructure.Settings;
using MySqlConnector;

namespace Goldata.Infrastructure.Legacy;

public sealed record TodoItem(
    long HistoryId, string Module, string ModuleTitle, long RecordId, string Title, string PersonFrom, string PersonTo,
    string StatusTo, string Comment, string Timestamp);

/// <summary>
/// 待办事项 (REQ-WF-3, 002-spec §3.10): the latest <c>historys</c> row of each record decides who holds it,
/// exactly as legacy <c>admin-index.php</c>.
/// </summary>
public sealed class TodoService(Db db, RecordService records, SettingsService settings)
{
    private const string LatestSql = """
        SELECT h.id, h.table_name, h.table_id, h.person_from, h.person_to, h.status_to, h.comment, h.timestamp
        FROM historys h
        JOIN (SELECT MAX(id) AS max_id FROM historys GROUP BY table_name, table_id) last ON last.max_id = h.id
        """;

    // A class, not a positional record: Dapper converts column types (int → long, NULL) for settable properties,
    // but a positional constructor must match the column types exactly.
    private sealed class Row
    {
        public long Id { get; set; }
        public string Table_name { get; set; } = "";
        public long? Table_id { get; set; }
        public string Person_from { get; set; } = "";
        public string Person_to { get; set; } = "";
        public string Status_to { get; set; } = "";
        public string Comment { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }

    public async Task<IReadOnlyList<TodoItem>> MineAsync(ICurrentUser user, CancellationToken ct)
    {
        await using var c = await db.LegacyAsync(ct);
        var rows = await c.QueryAsync<Row>(new CommandDefinition(LatestSql + " WHERE h.person_to = @me ORDER BY h.timestamp DESC",
            new { me = LegacyText.EncodeCompat(user.DisplayName) }, cancellationToken: ct));
        return await EnrichAsync(rows, user, ct);
    }

    /// <summary>Other people's to-dos (legacy: <c>person_to NOT LIKE '%me%' AND NOT LIKE '%部%'</c>).</summary>
    public async Task<IReadOnlyList<TodoItem>> OthersAsync(ICurrentUser user, CancellationToken ct)
    {
        user.Require(PermissionCatalog.TodoAll);
        await using var c = await db.LegacyAsync(ct);
        var rows = await c.QueryAsync<Row>(new CommandDefinition(LatestSql + """
             WHERE h.person_to NOT LIKE @me AND h.person_to NOT LIKE '%部%' AND h.person_to <> ''
             ORDER BY h.person_to, h.timestamp DESC
            """, new { me = "%" + LegacyText.EscapeLike(LegacyText.EncodeCompat(user.DisplayName)) + "%" }, cancellationToken: ct));
        return await EnrichAsync(rows, user, ct);
    }

    /// <summary>Records currently waiting on <paramref name="displayName"/> (used by the user delete check, REQ-ADM-6).</summary>
    public static async Task<IReadOnlyList<(string Table, long Id)>> OpenFor(MySqlConnection c, string displayName, CancellationToken ct) =>
        (await c.QueryAsync<(string Table, long? Id)>(new CommandDefinition(
            "SELECT h.table_name, CAST(h.table_id AS SIGNED) FROM historys h JOIN (SELECT MAX(id) AS max_id FROM historys GROUP BY table_name, table_id) last ON last.max_id = h.id WHERE h.person_to = @name",
            new { name = LegacyText.EncodeCompat(displayName) }, cancellationToken: ct)))
        .Where(r => r.Id is not null).Select(r => (r.Table, r.Id!.Value)).ToList();

    private async Task<IReadOnlyList<TodoItem>> EnrichAsync(IEnumerable<Row> rows, ICurrentUser user, CancellationToken ct)
    {
        var hrHidden = user.Channel == Channel.Mcp && !await settings.GetBoolAsync(SettingsService.McpHrEnabled, ct);
        var result = new List<TodoItem>();
        foreach (var group in rows.Where(r => r.Table_id is not null).GroupBy(r => r.Table_name))
        {
            var m = ModuleRegistry.FindByTable(group.Key);
            // Filtered by the viewer's read permission per table; HR items show as 权限不足 in the legacy app, here they are omitted.
            if (m is null || !user.Has(PermissionCatalog.Code(m.Key, "read")) || (m.IsHr && hrHidden)) continue;
            var titles = await records.TitlesAsync(m, group.Select(r => r.Table_id!.Value).Distinct().ToList(), ct);
            result.AddRange(group.Select(r => new TodoItem(r.Id, m.Key, m.Title, r.Table_id!.Value, titles.GetValueOrDefault(r.Table_id!.Value, $"#{r.Table_id}"),
                LegacyText.Decode(r.Person_from), LegacyText.Decode(r.Person_to), LegacyText.Decode(r.Status_to),
                LegacyText.Decode(r.Comment), r.Timestamp.ToString("yyyy-MM-dd HH:mm"))));
        }
        return result.OrderByDescending(t => t.Timestamp).ToList();
    }
}
