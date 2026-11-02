using System.Net;
using System.Text;

namespace Goldata.Domain;

/// <summary>
/// Text rules shared with the legacy PHP app (002-spec §3.3).
/// </summary>
public static class LegacyText
{
    /// <summary>
    /// Encodes exactly like PHP 8.0 <c>htmlspecialchars($s)</c> with its default <c>ENT_COMPAT</c>:
    /// <c>&amp; " &lt; &gt;</c> are encoded, the single quote is left as is, and existing entities are double-encoded.
    /// </summary>
    public static string EncodeCompat(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        var sb = new StringBuilder(value.Length + 16);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '&': sb.Append("&amp;"); break;
                case '"': sb.Append("&quot;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default: sb.Append(ch); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Decodes entities written by PHP (or by <see cref="EncodeCompat"/>) for display.</summary>
    public static string Decode(string? value) => string.IsNullOrEmpty(value) ? value ?? "" : WebUtility.HtmlDecode(value);

    /// <summary>True when the text has code points above U+FFFF, which the utf8 (utf8mb3) columns cannot store.</summary>
    public static bool HasSupplementaryChars(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        foreach (var ch in value)
            if (char.IsSurrogate(ch)) return true;
        return false;
    }

    /// <summary>Escapes <c>\ % _</c> for use inside a MySQL <c>LIKE</c> pattern (default escape char <c>\</c>).</summary>
    public static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    /// <summary>Contains-pattern for a user filter term, encoded like the stored data.</summary>
    public static string ContainsPattern(string term) => "%" + EscapeLike(EncodeCompat(term.Trim())) + "%";
}
