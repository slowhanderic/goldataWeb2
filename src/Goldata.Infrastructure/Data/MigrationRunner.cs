using System.Reflection;
using DbUp;
using DbUp.Engine;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Goldata.Infrastructure.Data;

/// <summary>
/// Applies the numbered scripts from <c>db/migrations</c> (embedded in this assembly) with DbUp — <c>goldata.sh migrate</c>.
/// Both journals live in the app database (<c>goldata_app</c>, or e.g. <c>goldata_app_uat</c>), so the legacy database
/// gets no new table (db/README.md, REQ-CMP-1). Database names come from the env file (DB_APP_NAME / DB_LEGACY_NAME).
/// </summary>
public static class MigrationRunner
{
    public const string AppPrefix = "migrations.goldata_app.";
    public const string LegacyPrefix = "migrations.goldata.";

    public static IReadOnlyList<string> EmbeddedScripts(string prefix) =>
        typeof(MigrationRunner).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToList();

    /// <param name="migratorConnectionString">The migrator account; its Database is the app database.</param>
    /// <param name="legacyDatabase">The legacy database name (<c>goldata</c> in prod).</param>
    public static int Run(string migratorConnectionString, string legacyDatabase, ILogger log)
    {
        var appCs = new MySqlConnectionStringBuilder(migratorConnectionString) { AllowUserVariables = true };
        var appDb = appCs.Database;
        if (string.IsNullOrWhiteSpace(appDb) || string.Equals(appDb, legacyDatabase, StringComparison.OrdinalIgnoreCase))
        {
            // REQ-CMP-1: the new tables must never be created inside the legacy database.
            log.LogError("Refusing to migrate: DB_APP_NAME ('{App}') must be a separate database from DB_LEGACY_NAME ('{Legacy}'), e.g. goldata_app.",
                appDb, legacyDatabase);
            return 2;
        }
        var legacyCs = new MySqlConnectionStringBuilder(appCs.ConnectionString) { Database = legacyDatabase };
        var asm = Assembly.GetExecutingAssembly();

        var steps = new (string Name, string ConnectionString, string Prefix, string Journal)[]
        {
            (appDb, appCs.ConnectionString, AppPrefix, "schemaversions"),
            (legacyDatabase, legacyCs.ConnectionString, LegacyPrefix, "schemaversions_goldata"),
        };

        foreach (var step in steps)
        {
            if (EmbeddedScripts(step.Prefix).Count == 0) { log.LogInformation("No {Db} migrations.", step.Name); continue; }

            var upgrader = DeployChanges.To
                .MySqlDatabase(step.ConnectionString)
                .WithScriptsEmbeddedInAssembly(asm, n => n.StartsWith(step.Prefix, StringComparison.Ordinal))
                .JournalToMySqlTable(appDb, step.Journal)
                .WithTransactionPerScript()
                .LogToConsole()
                .Build();

            var pending = upgrader.GetScriptsToExecute().Select(s => s.Name).ToList();
            log.LogInformation("{Db}: {Count} pending script(s): {Scripts}", step.Name, pending.Count, string.Join(", ", pending));

            DatabaseUpgradeResult result = upgrader.PerformUpgrade();
            if (!result.Successful)
            {
                log.LogError(result.Error, "{Db}: migration failed at {Script}", step.Name, result.ErrorScript?.Name);
                return 1;
            }
        }
        log.LogInformation("Migrations complete.");
        return 0;
    }
}
