using Goldata.Application.Records;
using Goldata.Domain;

namespace Goldata.Infrastructure.Data;

/// <summary>Turns a Dapper row into JSON-friendly values with the legacy read rules (002-spec §3.3).</summary>
public static class RowMapper
{
    public static Dictionary<string, object?> Map(object row, TableSchema schema)
    {
        var src = (IDictionary<string, object?>)row;
        var dst = new Dictionary<string, object?>(src.Count, StringComparer.Ordinal);
        foreach (var (key, value) in src)
            dst[key] = Normalize(value, schema.Find(key)?.Kind ?? ColumnKind.Text);
        return dst;
    }

    public static object? Normalize(object? value, ColumnKind kind) => value switch
    {
        null or DBNull => null,
        DateTime d when d == DateTime.MinValue || d.Year < 1000 => null,     // '0000-00-00' → empty
        DateTime d => kind == ColumnKind.Date ? d.ToString("yyyy-MM-dd") : d.ToString("yyyy-MM-dd HH:mm:ss"),
        double x when double.IsNaN(x) || double.IsInfinity(x) => null,
        double x => Math.Round(x, 6),
        float x => Math.Round((double)x, 6),
        string s when kind == ColumnKind.Json => s,
        string s => LegacyText.Decode(s),
        _ => value,
    };

    /// <summary>Display title of a record from its module's title columns, e.g. "合同名 / HT-001".</summary>
    public static string Title(IReadOnlyDictionary<string, object?> record, IEnumerable<string> titleColumns, object? id)
    {
        var parts = titleColumns.Select(c => record.GetValueOrDefault(c)?.ToString())
            .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        return parts.Count > 0 ? string.Join(" / ", parts) : $"#{id}";
    }
}
