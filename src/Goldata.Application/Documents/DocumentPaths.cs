using System.Text;
using Goldata.Domain;

namespace Goldata.Application.Documents;

/// <summary>Path and name rules for the shared <c>pdf_folder</c> (002-spec §4).</summary>
public static class DocumentPaths
{
    public const string Root = "pdf_folder";
    public const string RecycleFolder = "_recycle";

    public static readonly IReadOnlySet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".jpeg", ".jpg", ".png", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".dwg", ".dxf", ".zip", ".rar" };

    private const string ForbiddenChars = "/\\:*?\"<>|#%$!&~`;";

    public static readonly TimeZoneInfo ChinaTime = FindChinaTime();

    private static TimeZoneInfo FindChinaTime()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("CST", TimeSpan.FromHours(8), "CST", "CST"); }
    }

    /// <summary>Validates and NFC-normalises an uploaded file name (002-spec §4.3 steps 2–3).</summary>
    public static string NormalizeFileName(string? fileName)
    {
        var name = Path.GetFileName((fileName ?? "").Replace('\\', '/')).Trim().Normalize(NormalizationForm.FormC);
        if (name.Length == 0 || name.Length > 200) throw AppError.Validation("文件名为空或过长");
        if (name.Contains("..") || name.StartsWith('.')) throw AppError.Validation("文件名不合法");
        if (name.Any(ch => ForbiddenChars.Contains(ch) || char.IsControl(ch)))
            throw AppError.Validation("文件名不能包含 / \\ : * ? \" < > | # % $ ! & ~ ` ;");
        if (LegacyText.HasSupplementaryChars(name)) throw AppError.Validation("文件名不支持表情符号或生僻字");
        if (!AllowedExtensions.Contains(Path.GetExtension(name)))
            throw AppError.Validation("只允许上传 jpeg/jpg/png/pdf/doc/docx/xls/xlsx/dwg/dxf/zip/rar 文件");
        return name;
    }

    /// <summary><c>pdf_folder/{folder}/{yyyyMM}/{fileName}</c>, month in Asia/Shanghai time (legacy format).</summary>
    public static string BuildRelativePath(string folder, string fileName, DateTimeOffset now)
    {
        var month = TimeZoneInfo.ConvertTime(now, ChinaTime).ToString("yyyyMM");
        return $"{Root}/{folder}/{month}/{fileName}";
    }

    /// <summary><c>name (2).ext</c>, <c>name (3).ext</c>, … for AutoRename.</summary>
    public static string Suggest(string fileName, int n) =>
        $"{Path.GetFileNameWithoutExtension(fileName)} ({n}){Path.GetExtension(fileName)}";

    /// <summary>
    /// Resolves a stored <c>file_path</c> value under the document root, refusing anything that escapes it
    /// (NFR-SEC-5). Returns <c>null</c> for unsafe paths.
    /// </summary>
    public static string? ResolveUnderRoot(string rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var rel = relativePath.Replace('\\', '/').TrimStart('/');
        if (rel.StartsWith("../", StringComparison.Ordinal)) rel = rel[3..];  // tolerate legacy "../pdf_folder/..."
        if (!rel.StartsWith(Root + "/", StringComparison.Ordinal)) return null;
        var root = Path.GetFullPath(rootPath);
        var full = Path.GetFullPath(Path.Combine(root, rel));
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return full.StartsWith(rootWithSep, StringComparison.Ordinal) ? full : null;
    }

    public static string ContentType(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".zip" => "application/zip",
        ".rar" => "application/vnd.rar",
        ".dwg" => "image/vnd.dwg",
        ".dxf" => "image/vnd.dxf",
        _ => "application/octet-stream",
    };

    /// <summary>Magic-byte check for the formats where it is reliable (002-spec §4.3 step 2).</summary>
    public static bool LooksLike(string fileName, ReadOnlySpan<byte> head)
    {
        static bool Starts(ReadOnlySpan<byte> h, params byte[] sig) => h.Length >= sig.Length && h[..sig.Length].SequenceEqual(sig);
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => Starts(head, 0x25, 0x50, 0x44, 0x46),                         // %PDF
            ".png" => Starts(head, 0x89, 0x50, 0x4E, 0x47),
            ".jpg" or ".jpeg" => Starts(head, 0xFF, 0xD8, 0xFF),
            ".zip" or ".docx" or ".xlsx" => Starts(head, 0x50, 0x4B),              // PK
            ".doc" or ".xls" => Starts(head, 0xD0, 0xCF, 0x11, 0xE0),              // OLE2
            ".rar" => Starts(head, 0x52, 0x61, 0x72, 0x21),                         // Rar!
            _ => true,                                                              // dwg/dxf: no reliable signature
        };
    }
}
