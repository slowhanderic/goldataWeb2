using System.Security.Cryptography;
using System.Text;
using Dapper;
using Goldata.Domain;
using Goldata.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Goldata.Infrastructure.Identity;

public sealed record ApiTokenInfo(int Id, string? Name, string Scopes, DateTime ExpiresAt, DateTime? LastUsedAt, DateTime? RevokedAt, DateTime CreatedAt);

public sealed record ValidatedToken(int TokenId, int UserId, IReadOnlySet<string> Scopes);

/// <summary>Personal access tokens for MCP (002-spec §5.3). Only a peppered SHA-256 of each token is stored.</summary>
public sealed class TokenService(Db db, IMemoryCache cache, IOptions<AuthOptions> options, TimeProvider clock)
{
    public const string Prefix = "gd_";
    public static readonly IReadOnlySet<string> ValidScopes = new HashSet<string> { "mcp:read", "mcp:write" };

    private readonly AuthOptions _o = options.Value;

    public string HashToken(string token)
    {
        if (string.IsNullOrEmpty(_o.TokenPepper)) throw new InvalidOperationException("TOKEN_PEPPER is not set in the env file (deploy/env/*.env).");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token + _o.TokenPepper)));
    }

    public async Task<(ApiTokenInfo Info, string Token)> CreateAsync(int userId, string? name, IReadOnlyCollection<string> scopes, int days, CancellationToken ct)
    {
        if (scopes.Count == 0 || scopes.Any(s => !ValidScopes.Contains(s))) throw AppError.Validation("令牌范围只能是 mcp:read / mcp:write");
        days = Math.Clamp(days, 1, _o.MaxTokenDays);
        var token = Prefix + Base64Url(RandomNumberGenerator.GetBytes(32));
        var expires = clock.GetLocalNow().DateTime.AddDays(days);
        await using var c = await db.AppAsync(ct);
        var id = await c.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO app_api_tokens (user_id, name, token_hash, scopes, expires_at) VALUES (@userId, @name, @hash, @scopes, @expires);
            SELECT LAST_INSERT_ID();
            """, new { userId, name = name?.Trim(), hash = HashToken(token), scopes = string.Join(' ', scopes.Order()), expires }, cancellationToken: ct));
        var info = (await ListAsync(userId, ct)).Single(t => t.Id == id);
        return (info, token);
    }

    public async Task<IReadOnlyList<ApiTokenInfo>> ListAsync(int userId, CancellationToken ct)
    {
        await using var c = await db.AppAsync(ct);
        return (await c.QueryAsync<ApiTokenInfo>(new CommandDefinition("""
            SELECT id, name, scopes, expires_at AS ExpiresAt, last_used_at AS LastUsedAt, revoked_at AS RevokedAt, created_at AS CreatedAt
            FROM app_api_tokens WHERE user_id = @userId ORDER BY created_at DESC
            """, new { userId }, cancellationToken: ct))).ToList();
    }

    public async Task<bool> RevokeAsync(int userId, int tokenId, CancellationToken ct)
    {
        await using var c = await db.AppAsync(ct);
        var n = await c.ExecuteAsync(new CommandDefinition(
            "UPDATE app_api_tokens SET revoked_at = NOW() WHERE id = @tokenId AND user_id = @userId AND revoked_at IS NULL",
            new { tokenId, userId }, cancellationToken: ct));
        cache.Remove("tok:" + tokenId);
        return n > 0;
    }

    /// <summary>Returns the token's user and scopes when it is known, not revoked and not expired.</summary>
    public async Task<ValidatedToken?> ValidateAsync(string token, CancellationToken ct)
    {
        if (!token.StartsWith(Prefix, StringComparison.Ordinal) || token.Length > 100) return null;
        var hash = HashToken(token);
        await using var c = await db.AppAsync(ct);
        var row = await c.QuerySingleOrDefaultAsync<(int Id, int UserId, string Scopes, DateTime ExpiresAt, DateTime? RevokedAt, DateTime? LastUsedAt)>(
            new CommandDefinition("""
                SELECT id, user_id, scopes, expires_at, revoked_at, last_used_at FROM app_api_tokens WHERE token_hash = @hash
                """, new { hash }, cancellationToken: ct));
        var now = clock.GetLocalNow().DateTime;
        if (row.Id == 0 || row.RevokedAt is not null || row.ExpiresAt <= now) return null;

        if (row.LastUsedAt is null || now - row.LastUsedAt > TimeSpan.FromMinutes(5))
            await c.ExecuteAsync(new CommandDefinition("UPDATE app_api_tokens SET last_used_at = NOW() WHERE id = @id", new { id = row.Id }, cancellationToken: ct));

        return new ValidatedToken(row.Id, row.UserId, row.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet());
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
