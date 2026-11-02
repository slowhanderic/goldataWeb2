using System.Data;
using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace Goldata.Infrastructure.Data;

/// <summary>Opens connections to the legacy <c>goldata</c> DB and the new <c>goldata_app</c> DB (002-spec §3.1).</summary>
public sealed class Db(IConfiguration config)
{
    private readonly string _legacy = Required(config, "Legacy");
    private readonly string _app = Required(config, "App");

    private static string Required(IConfiguration config, string name) =>
        config.GetConnectionString(name) is { Length: > 0 } cs
            ? cs
            : throw new InvalidOperationException(
                $"Connection string '{name}' is missing: set DB_HOST / DB_API_USER / DB_API_PASSWORD in the env file (deploy/env/*.env).");

    public async Task<MySqlConnection> LegacyAsync(CancellationToken ct = default) => await OpenAsync(_legacy, ct);
    public async Task<MySqlConnection> AppAsync(CancellationToken ct = default) => await OpenAsync(_app, ct);

    private static async Task<MySqlConnection> OpenAsync(string cs, CancellationToken ct)
    {
        var c = new MySqlConnection(cs);
        await c.OpenAsync(ct);
        return c;
    }
}

public static class DbExtensions
{
    public static async Task<MySqlTransaction> BeginAsync(this MySqlConnection c, CancellationToken ct = default) =>
        await c.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
}
