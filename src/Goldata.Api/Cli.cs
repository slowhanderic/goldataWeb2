using Goldata.Infrastructure.Data;
using Goldata.Infrastructure.Identity;

namespace Goldata.Api;

/// <summary>
/// One-shot modes of the API image, used by <c>deploy/goldata.sh</c>:
/// <c>--migrate</c>, <c>--import-users &lt;login.php&gt;</c>, <c>--healthcheck</c>.
/// </summary>
public static class Cli
{
    public static async Task<int?> TryRunAsync(string[] args)
    {
        if (args.Length == 0) return null;
        switch (args[0])
        {
            case "--healthcheck":
                return await HealthcheckAsync();
            case "--migrate":
            {
                var (config, log) = Bootstrap("migrate");
                var cs = config.GetConnectionString("Migrator");
                if (string.IsNullOrEmpty(cs)) { log.LogError("DB_MIGRATOR_USER / DB_MIGRATOR_PASSWORD are missing in the env file."); return 2; }
                return MigrationRunner.Run(cs, config["Db:LegacyDatabase"] ?? "goldata", log);
            }
            case "--import-users":
            {
                var (config, log) = Bootstrap("import-users");
                if (args.Length < 2 || !File.Exists(args[1])) { log.LogError("Usage: --import-users /import/login.php"); return 2; }
                var cs = config.GetConnectionString("App");
                if (string.IsNullOrEmpty(cs)) { log.LogError("ConnectionStrings:App is missing."); return 2; }
                return await LegacyUserImporter.RunAsync(cs, args[1], log);
            }
            default:
                return null;
        }
    }

    private static (IConfiguration Config, ILogger Log) Bootstrap(string name)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .AddInMemoryCollection(GoldataEnv.FromProcess())   // the env file (already loaded into the process)
            .Build();
        var log = LoggerFactory.Create(b => b.AddSimpleConsole(o => o.SingleLine = true)).CreateLogger(name);
        return (config, log);
    }

    /// <summary>Docker HEALTHCHECK without curl: GET /health/live on the local port.</summary>
    private static async Task<int> HealthcheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            var port = Environment.GetEnvironmentVariable("GOLDATA_HEALTH_PORT") ?? "8080";
            var res = await http.GetAsync($"http://127.0.0.1:{port}/health/live");
            return res.IsSuccessStatusCode ? 0 : 1;
        }
        catch
        {
            return 1;
        }
    }
}
