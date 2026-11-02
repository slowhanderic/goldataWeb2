using Dapper;
using Goldata.Application.Security;
using Goldata.Domain;
using MySqlConnector;

namespace Goldata.Infrastructure.Legacy;

/// <summary>
/// Writes <c>update_logs</c> exactly like the legacy <c>commit_sql()</c> (002-spec §3.9, REQ-AUD-1/2), so the
/// legacy <c>show_logs()</c> keeps showing a continuous history.
/// </summary>
public static class LegacyAuditWriter
{
    public const string Added = "添加记录 ";
    public const string Updated = "更新记录 ";
    public const string Deleted = "删除记录 ";

    public static Task WriteAsync(MySqlConnection c, MySqlTransaction tx, string table, long rowId, string sqlText,
        ICurrentUser user, string tag, CancellationToken ct)
    {
        var fullTag = user.Channel == Channel.Mcp ? tag + "[MCP]" : tag;
        return c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO update_logs (table_name, row_id, sql_text, user, update_time, tag)
            VALUES (@table, @rowId, @sqlText, @user, NOW(), @tag)
            """, new { table, rowId, sqlText = LegacyText.EncodeCompat(sqlText), user = user.AuditUser(), tag = fullTag }, tx,
            cancellationToken: ct));
    }

    /// <summary>A readable statement for the log (values inlined for display only; never executed).</summary>
    public static string Render(string verb, string table, IEnumerable<KeyValuePair<string, object?>> values, string? where = null)
    {
        static string V(object? v) => v switch
        {
            null => "NULL",
            string s => "'" + s.Replace("'", "''") + "'",
            DateTime d => "'" + d.ToString("yyyy-MM-dd HH:mm:ss") + "'",
            bool b => b ? "1" : "0",
            _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "NULL",
        };
        var kv = values.ToList();
        return verb switch
        {
            "INSERT" => $"INSERT INTO {table} ({string.Join(", ", kv.Select(x => x.Key))}) VALUES ({string.Join(", ", kv.Select(x => V(x.Value)))})",
            "UPDATE" => $"UPDATE {table} SET {string.Join(", ", kv.Select(x => $"{x.Key} = {V(x.Value)}"))} WHERE {where}",
            "DELETE" => $"DELETE FROM {table} WHERE {where}",
            _ => throw new ArgumentOutOfRangeException(nameof(verb)),
        };
    }
}
