using Dapper;
using Goldata.Application.Permissions;
using Goldata.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;
using MySqlConnector;

namespace Goldata.Infrastructure.Identity;

/// <summary>
/// Loads a user with their effective permissions (002-spec §5.2). Cached for 30 s per user; any admin change
/// calls <see cref="InvalidateAll"/>, and a changed security stamp always forces a reload (§5.3).
/// </summary>
public sealed class UserContextService(Db db, IMemoryCache cache)
{
    private static CancellationTokenSource _generation = new();

    public static void InvalidateAll()
    {
        var old = Interlocked.Exchange(ref _generation, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }

    /// <summary>Returns null when the user no longer exists, is disabled, or the stamp changed.</summary>
    public async Task<UserContext?> GetValidAsync(int userId, string stamp, CancellationToken ct = default)
    {
        var ctx = await GetAsync(userId, ct);
        if (ctx is null || !ctx.IsActive || !string.Equals(ctx.SecurityStamp, stamp, StringComparison.Ordinal)) return null;
        return ctx;
    }

    public async Task<UserContext?> GetAsync(int userId, CancellationToken ct = default)
    {
        var key = "uctx:" + userId;
        if (cache.TryGetValue(key, out UserContext? hit)) return hit;

        await using var c = await db.AppAsync(ct);
        var ctx = await LoadAsync(c, null, userId, ct);
        if (ctx is not null)
        {
            var options = new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30) }
                .AddExpirationToken(new CancellationChangeToken(_generation.Token));
            cache.Set(key, ctx, options);
        }
        return ctx;
    }

    public static async Task<UserContext?> LoadAsync(MySqlConnection c, MySqlTransaction? tx, int userId, CancellationToken ct)
    {
        var u = await c.QuerySingleOrDefaultAsync<AppUser>(new CommandDefinition(
            AppUser.SelectSql + " WHERE u.id = @userId", new { userId }, tx, cancellationToken: ct));
        if (u is null) return null;
        var perms = await PermissionsForAsync(c, tx, u, ct);
        return new UserContext(u.Id, u.Login, u.DisplayName, u.RoleName, u.Rank, u.RoleIsReadOnly,
            u.MustChangePassword, u.IsActive, u.SecurityStamp, perms);
    }

    public static async Task<IReadOnlySet<string>> PermissionsForAsync(MySqlConnection c, MySqlTransaction? tx, AppUser u, CancellationToken ct) =>
        EffectivePermissions.Compute(u.Rank, u.RoleIsReadOnly,
            await RoleGrantsAsync(c, tx, u.RoleId, ct), await OverridesAsync(c, tx, u.Id, ct));

    public static async Task<IReadOnlyList<RoleGrant>> RoleGrantsAsync(MySqlConnection c, MySqlTransaction? tx, int roleId, CancellationToken ct) =>
        (await c.QueryAsync<RoleGrant>(new CommandDefinition(
            "SELECT `rank` AS Rank, permission AS Permission FROM app_role_permissions WHERE role_id = @roleId",
            new { roleId }, tx, cancellationToken: ct))).ToList();

    public static async Task<IReadOnlyList<UserOverride>> OverridesAsync(MySqlConnection c, MySqlTransaction? tx, int userId, CancellationToken ct) =>
        (await c.QueryAsync<UserOverride>(new CommandDefinition(
            "SELECT permission AS Permission, allow AS Allow FROM app_user_permission_overrides WHERE user_id = @userId",
            new { userId }, tx, cancellationToken: ct))).ToList();
}
