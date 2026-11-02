using Dapper;
using Goldata.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;

namespace Goldata.Infrastructure.Settings;

/// <summary><c>goldata_app.app_settings</c>, cached for 30 s.</summary>
public sealed class SettingsService(Db db, IMemoryCache cache)
{
    public const string McpHrEnabled = "mcp.hr.enabled";

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        var all = await cache.GetOrCreateAsync("app_settings", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            await using var c = await db.AppAsync(ct);
            return (await c.QueryAsync<(string Key, string? Value)>(new CommandDefinition(
                "SELECT `key`, value FROM app_settings", cancellationToken: ct))).ToDictionary(x => x.Key, x => x.Value);
        });
        return all!.GetValueOrDefault(key);
    }

    public async Task<bool> GetBoolAsync(string key, CancellationToken ct = default) =>
        string.Equals(await GetAsync(key, ct), "true", StringComparison.OrdinalIgnoreCase);
}
