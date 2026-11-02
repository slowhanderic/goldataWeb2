using MySqlConnector;

namespace Goldata.Api;

/// <summary>
/// ONE env file per environment (<c>deploy/env/{local,uat,prod}.env</c>) holds every setting, secrets included.
/// This class is the single place where those keys become .NET configuration:
/// <list type="bullet">
///   <item>local: <c>dotnet run</c> loads <c>deploy/env/local.env</c> (or <c>GOLDATA_ENV_FILE</c>) into the process environment;</item>
///   <item>uat/prod: docker-compose passes the NAS <c>.env</c> to the container with <c>env_file</c>.</item>
/// </list>
/// Variables already set in the process (shell, compose <c>environment:</c>) win over the file.
/// </summary>
public static class GoldataEnv
{
    /// <summary>Keys whose relative values ("./", "../") are resolved against the env file's folder.</summary>
    private static readonly HashSet<string> PathKeys = ["DOCUMENTS_ROOT", "DATA_PROTECTION_KEYS", "LOG_FILE"];

    /// <summary>Loads <c>GOLDATA_ENV_FILE</c>, else the nearest <c>deploy/env/local.env</c> above the working directory (dev only).</summary>
    public static string? LoadDefaultFile()
    {
        var path = Environment.GetEnvironmentVariable("GOLDATA_ENV_FILE");
        if (string.IsNullOrEmpty(path))
        {
            for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "deploy", "env", "local.env");
                if (File.Exists(candidate)) { path = candidate; break; }
            }
        }
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        foreach (var (key, value) in Parse(File.ReadAllLines(path), Path.GetDirectoryName(Path.GetFullPath(path))!))
            if (Environment.GetEnvironmentVariable(key) is null) Environment.SetEnvironmentVariable(key, value);
        return path;
    }

    /// <summary><c>KEY=VALUE</c> lines; <c>#</c> comments; optional single/double quotes; <c>export </c> prefix allowed.</summary>
    public static IEnumerable<KeyValuePair<string, string>> Parse(IEnumerable<string> lines, string? baseDir = null)
    {
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0]) value = value[1..^1];
            else if (value.IndexOf(" #", StringComparison.Ordinal) is var hash and >= 0) value = value[..hash].TrimEnd();   // inline comment
            if (baseDir is not null && PathKeys.Contains(key) && (value.StartsWith("./") || value.StartsWith("../")))
                value = Path.GetFullPath(Path.Combine(baseDir, value));
            yield return new(key, value);
        }
    }

    /// <summary>Maps the env keys to .NET configuration keys. Unset keys are left out, so appsettings.json defaults apply.</summary>
    public static Dictionary<string, string?> ToConfiguration(Func<string, string?> get)
    {
        var c = new Dictionary<string, string?>();
        void Set(string configKey, string envKey) { if (get(envKey) is { Length: > 0 } v) c[configKey] = v; }

        if (get("DB_HOST") is { Length: > 0 } host)
        {
            var port = uint.TryParse(get("DB_PORT"), out var p) ? p : 3307;
            var legacyDb = get("DB_LEGACY_NAME") is { Length: > 0 } l ? l : "goldata";
            var appDb = get("DB_APP_NAME") is { Length: > 0 } a ? a : "goldata_app";
            string Cs(string db, string? user, string? password, bool legacy) => new MySqlConnectionStringBuilder
            {
                Server = host, Port = port, Database = db, UserID = user ?? "", Password = password ?? "",
                CharacterSet = legacy ? "utf8" : "utf8mb4", ConvertZeroDateTime = legacy, TreatTinyAsBoolean = !legacy,
                ConnectionTimeout = 10,
            }.ConnectionString;

            if (get("DB_API_USER") is { Length: > 0 } apiUser)
            {
                c["ConnectionStrings:Legacy"] = Cs(legacyDb, apiUser, get("DB_API_PASSWORD"), legacy: true);
                c["ConnectionStrings:App"] = Cs(appDb, apiUser, get("DB_API_PASSWORD"), legacy: false);
            }
            if (get("DB_MIGRATOR_USER") is { Length: > 0 } migUser)
                c["ConnectionStrings:Migrator"] = Cs(appDb, migUser, get("DB_MIGRATOR_PASSWORD"), legacy: false);
            c["Db:LegacyDatabase"] = legacyDb;
        }

        Set("Documents:RootPath", "DOCUMENTS_ROOT");
        if (long.TryParse(get("DOCUMENTS_MAX_MB"), out var mb)) c["Documents:MaxBytes"] = (mb * 1024 * 1024).ToString();
        Set("LegacyCompat:EncodeHtmlOnWrite", "LEGACY_ENCODE_HTML");
        Set("LegacyCompat:Utf8mb4", "DB_UTF8MB4");
        Set("Auth:TokenPepper", "TOKEN_PEPPER");
        Set("Auth:PublicBaseUrl", "PUBLIC_BASE_URL");
        Set("Auth:SessionIdleMinutes", "SESSION_IDLE_MINUTES");
        Set("Auth:LockoutAttempts", "LOCKOUT_ATTEMPTS");
        Set("Auth:LockoutMinutes", "LOCKOUT_MINUTES");
        Set("Auth:MaxTokenDays", "MAX_TOKEN_DAYS");
        Set("DataProtection:KeysPath", "DATA_PROTECTION_KEYS");
        Set("ForwardedHeaders:KnownNetworks", "FORWARDED_NETWORKS");
        Set("Serilog:MinimumLevel:Default", "LOG_LEVEL");
        Set("Serilog:File:Path", "LOG_FILE");
        Set("Goldata:Environment", "GOLDATA_ENV");
        return c;
    }

    public static Dictionary<string, string?> FromProcess() => ToConfiguration(Environment.GetEnvironmentVariable);
}
