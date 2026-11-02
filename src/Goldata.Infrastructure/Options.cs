namespace Goldata.Infrastructure;

/// <summary><c>Documents:*</c> settings (002-spec §4, §9.4).</summary>
public sealed class DocumentOptions
{
    /// <summary>The directory that CONTAINS <c>pdf_folder</c> (the PHP web root), e.g. <c>/data/docroot</c>.</summary>
    public string RootPath { get; set; } = "";
    public long MaxBytes { get; set; } = 83_886_080;   // 80 MB, as legacy upload_file.php
}

/// <summary><c>LegacyCompat:*</c> settings (002-spec §3.3).</summary>
public sealed class LegacyCompatOptions
{
    /// <summary>Encode text like PHP htmlspecialchars (ENT_COMPAT) on write while the PHP app is in use (Q7).</summary>
    public bool EncodeHtmlOnWrite { get; set; } = true;
    /// <summary>True only after the DB is converted to utf8mb4 (Q6).</summary>
    public bool Utf8mb4 { get; set; }
}

/// <summary><c>Auth:*</c> settings (002-spec §5.3).</summary>
public sealed class AuthOptions
{
    /// <summary>Secret mixed into API-token hashes (secrets file <c>Auth__TokenPepper</c>).</summary>
    public string TokenPepper { get; set; } = "";
    public int LockoutAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int SessionIdleMinutes { get; set; } = 480;
    public int MaxTokenDays { get; set; } = 90;
    /// <summary>Public base URL, used in the PHP change-password banner and MCP client config.</summary>
    public string PublicBaseUrl { get; set; } = "";
}
