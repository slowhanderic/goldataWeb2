using Goldata.Api;
using MySqlConnector;

namespace Goldata.UnitTests;

public class GoldataEnvTests
{
    [Fact]
    public void Parser_reads_key_values_like_docker_compose()
    {
        var kv = GoldataEnv.Parse(
        [
            "# comment",
            "",
            "GOLDATA_ENV=uat",
            "export DB_HOST=192.168.168.235",
            "DB_API_PASSWORD=a$b=c",          // '$' and '=' are literal, not expanded
            "QUOTED=\"x y\"",
            "  SPACED  = v ",
            "NOT A LINE",
        ]).ToDictionary(x => x.Key, x => x.Value);

        kv["GOLDATA_ENV"].ShouldBe("uat");
        kv["DB_HOST"].ShouldBe("192.168.168.235");
        kv["DB_API_PASSWORD"].ShouldBe("a$b=c");
        kv["QUOTED"].ShouldBe("x y");
        kv["SPACED"].ShouldBe("v");
        kv.ShouldNotContainKey("NOT A LINE");
    }

    [Fact]
    public void Relative_paths_resolve_against_the_env_file_folder()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "repo", "deploy", "env");
        var kv = GoldataEnv.Parse(["DOCUMENTS_ROOT=../../.dev/docroot", "LOG_LEVEL=../not-a-path"], baseDir).ToDictionary(x => x.Key, x => x.Value);
        kv["DOCUMENTS_ROOT"].ShouldBe(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "repo", ".dev", "docroot")));
        kv["LOG_LEVEL"].ShouldBe("../not-a-path");
    }

    [Fact]
    public void Env_keys_map_to_connection_strings_and_options()
    {
        var env = new Dictionary<string, string>
        {
            ["DB_HOST"] = "192.168.168.235", ["DB_PORT"] = "3307",
            ["DB_LEGACY_NAME"] = "goldata_uat", ["DB_APP_NAME"] = "goldata_app_uat",
            ["DB_API_USER"] = "goldata_uat_api", ["DB_API_PASSWORD"] = "p1",
            ["DB_MIGRATOR_USER"] = "goldata_uat_migrator", ["DB_MIGRATOR_PASSWORD"] = "p2",
            ["DOCUMENTS_MAX_MB"] = "80", ["TOKEN_PEPPER"] = "pep", ["PUBLIC_BASE_URL"] = "https://erp-uat.example.com",
            ["FORWARDED_NETWORKS"] = "172.30.11.0/24", ["GOLDATA_ENV"] = "uat",
        };
        var c = GoldataEnv.ToConfiguration(k => env.GetValueOrDefault(k));

        var legacy = new MySqlConnectionStringBuilder(c["ConnectionStrings:Legacy"]);
        legacy.Database.ShouldBe("goldata_uat");
        legacy.UserID.ShouldBe("goldata_uat_api");
        legacy.CharacterSet.ShouldBe("utf8");
        legacy.ConvertZeroDateTime.ShouldBeTrue();
        legacy.TreatTinyAsBoolean.ShouldBeFalse();

        var app = new MySqlConnectionStringBuilder(c["ConnectionStrings:App"]);
        app.Database.ShouldBe("goldata_app_uat");
        app.CharacterSet.ShouldBe("utf8mb4");

        var mig = new MySqlConnectionStringBuilder(c["ConnectionStrings:Migrator"]);
        mig.Database.ShouldBe("goldata_app_uat");
        mig.UserID.ShouldBe("goldata_uat_migrator");
        mig.Password.ShouldBe("p2");

        c["Db:LegacyDatabase"].ShouldBe("goldata_uat");
        c["Documents:MaxBytes"].ShouldBe("83886080");
        c["Auth:TokenPepper"].ShouldBe("pep");
        c["ForwardedHeaders:KnownNetworks"].ShouldBe("172.30.11.0/24");
    }

    [Fact]
    public void Unset_keys_are_left_to_appsettings_defaults() =>
        GoldataEnv.ToConfiguration(_ => null).ShouldBeEmpty();

    [Theory]
    [InlineData("local.env.example")]
    [InlineData("uat.env.example")]
    [InlineData("prod.env.example")]
    public void Templates_follow_the_compose_v1_safe_format(string file)
    {
        var path = Path.Combine(PermissionTests.RepoRoot(), "deploy", "env", file);
        foreach (var line in File.ReadAllLines(path).Where(l => l.Length > 0 && !l.StartsWith('#')))
        {
            line.ShouldMatch(@"^[A-Z_][A-Z0-9_]*=[^\s""']*$", $"{file}: '{line}' (no quotes, spaces or inline comments)");
        }
        var keys = GoldataEnv.Parse(File.ReadAllLines(path)).Select(k => k.Key).ToHashSet();
        keys.ShouldContain("GOLDATA_ENV");
        keys.ShouldContain("DB_HOST");
        keys.ShouldContain("TOKEN_PEPPER");
    }

    [Fact]
    public void Uat_and_prod_never_share_databases_ports_subnets_or_projects()
    {
        Dictionary<string, string> Load(string f) => GoldataEnv.Parse(
            File.ReadAllLines(Path.Combine(PermissionTests.RepoRoot(), "deploy", "env", f))).ToDictionary(x => x.Key, x => x.Value);
        var prod = Load("prod.env.example");
        var uat = Load("uat.env.example");
        foreach (var key in new[] { "COMPOSE_PROJECT_NAME", "WEB_PORT", "NETWORK_SUBNET", "DB_LEGACY_NAME", "DB_APP_NAME",
                     "DB_API_USER", "DB_MIGRATOR_USER", "DOC_ROOT", "TOKEN_PEPPER", "PHP_AUTH_CONFIG", "BACKUP_DIR" })
            uat[key].ShouldNotBe(prod[key], key);
    }
}
