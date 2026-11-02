using System.Globalization;
using Goldata.Application.Modules;
using Goldata.Domain;

namespace Goldata.Application.Records;

/// <summary>
/// Turns form input into the exact values the legacy PHP app writes (002-spec §3.3, REQ-CMP-2):
/// text is trimmed and htmlspecialchars-encoded (ENT_COMPAT), empty dates become <c>'0000-00-00'</c>,
/// empty amounts <c>0</c>. The server runs STRICT_TRANS_TABLES, so '' must never reach a date or double column.
/// </summary>
public static class WriteRules
{
    public const string ZeroDate = "0000-00-00";

    /// <summary>The editable fields of a module, restricted to columns that really exist.</summary>
    public static IReadOnlyList<(FieldSpec Spec, ColumnInfo Column)> EditableFields(ModuleDefinition m, TableSchema schema) =>
        (ModuleFields.Editable.GetValueOrDefault(m.Table) ?? [])
        .Select(f => (f, schema.Find(f.Column)))
        .Where(x => x.Item2 is not null)
        .Select(x => (x.f, x.Item2!))
        .ToList();

    /// <summary>
    /// Validates and converts the submitted values. Only editable columns are accepted; unknown keys are rejected
    /// so a client can't write ids, timestamps or status columns that only 转交 may change.
    /// </summary>
    public static Dictionary<string, object?> Normalize(
        ModuleDefinition m, TableSchema schema, IReadOnlyDictionary<string, string?> input, bool isCreate,
        bool utf8mb4 = false, bool encodeHtml = true)
    {
        var fields = EditableFields(m, schema).ToDictionary(x => x.Spec.Column, StringComparer.OrdinalIgnoreCase);
        foreach (var key in input.Keys)
            if (!fields.ContainsKey(key)) throw AppError.Validation($"字段不可编辑：{key}");

        var errors = new Dictionary<string, string>();
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (column, (spec, col)) in fields)
        {
            var present = input.TryGetValue(column, out var raw);
            if (!present && !isCreate) continue;                    // update: untouched fields stay as they are
            var text = (raw ?? "").Trim();
            if (spec.Required && text.Length == 0) { errors[column] = $"{col.Label}为必填项"; continue; }
            try
            {
                values[col.Name] = Convert(spec, col, text, utf8mb4, encodeHtml);
            }
            catch (FormatException e)
            {
                errors[column] = $"{col.Label}：{e.Message}";
            }
        }
        if (errors.Count > 0) throw AppError.Validation("请检查表单中的错误", errors);
        return values;
    }

    public static object? Convert(FieldSpec spec, ColumnInfo col, string text, bool utf8mb4 = false, bool encodeHtml = true)
    {
        switch (col.Kind)
        {
            case ColumnKind.Date:
            case ColumnKind.DateTime:
                if (text.Length == 0) return ZeroDate;
                if (!DateOnly.TryParseExact(text, ["yyyy-MM-dd", "yyyy/M/d", "yyyy-M-d"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    throw new FormatException("日期格式应为 YYYY-MM-DD");
                if (d.Year < 1900 || d.Year > 2100) throw new FormatException("日期超出范围");
                return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            case ColumnKind.Money:
                if (text.Length == 0) return 0d;
                var normalized = text.Replace(",", "").Replace("¥", "").Replace("，", "");
                if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var n))
                    throw new FormatException("请输入数字");
                if (Math.Abs(n) >= 1_000_000_000_000m) throw new FormatException("金额过大");
                return (double)Math.Round(n, 4);
            case ColumnKind.Int:
            case ColumnKind.Id:
                if (text.Length == 0) return null;
                if (!long.TryParse(text, out var i)) throw new FormatException("请输入整数");
                return i;
            default:
                if (!utf8mb4 && LegacyText.HasSupplementaryChars(text)) throw new FormatException("不支持表情符号或生僻字");
                if (text.Length > 20_000) throw new FormatException("内容过长");
                return encodeHtml ? LegacyText.EncodeCompat(text) : text;
        }
    }

    /// <summary>Columns whose new value differs from the current row (compared in stored form).</summary>
    public static Dictionary<string, object?> Changes(IReadOnlyDictionary<string, object?> values, IReadOnlyDictionary<string, object?> storedRow)
    {
        var changes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in values)
        {
            storedRow.TryGetValue(k, out var current);
            if (!SameStored(v, current)) changes[k] = v;
        }
        return changes;
    }

    private static bool SameStored(object? newValue, object? stored)
    {
        static string S(object? o) => o switch
        {
            null or DBNull => "",
            DateTime dt when dt == DateTime.MinValue || dt.Year < 1000 => ZeroDate,
            DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            double x => x.ToString("0.####", CultureInfo.InvariantCulture),
            float x => ((double)x).ToString("0.####", CultureInfo.InvariantCulture),
            decimal x => x.ToString("0.####", CultureInfo.InvariantCulture),
            _ => System.Convert.ToString(o, CultureInfo.InvariantCulture) ?? "",
        };
        return S(newValue) == S(stored);
    }
}
