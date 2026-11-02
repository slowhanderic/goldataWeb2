using Dapper;
using Goldata.Application.Identity;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Goldata.Infrastructure.Identity;

/// <summary>
/// One-time import of the hard-coded accounts from the legacy <c>login.php</c> (runbook step 7, 002-spec §5.1).
/// Existing bcrypt hashes are kept, so passwords don't change; every account must change its password at next login.
/// Accounts that already exist (same login) are skipped, so the import can be re-run safely.
/// </summary>
public static class LegacyUserImporter
{
    public static async Task<int> RunAsync(string appConnectionString, string loginPhpPath, ILogger log, CancellationToken ct = default)
    {
        var accounts = LegacyLoginParser.Parse(await File.ReadAllTextAsync(loginPhpPath, ct));
        log.LogInformation("Found {Count} accounts in {File}.", accounts.Count, Path.GetFileName(loginPhpPath));

        await using var c = new MySqlConnection(appConnectionString);
        await c.OpenAsync(ct);
        await using var tx = await c.BeginTransactionAsync(ct);
        var roles = (await c.QueryAsync<(int Id, string Name)>(new CommandDefinition("SELECT id, name FROM app_roles", transaction: tx, cancellationToken: ct)))
            .ToDictionary(r => r.Name, r => r.Id);
        var ranks = (await c.QueryAsync<string>(new CommandDefinition("SELECT name FROM app_ranks", transaction: tx, cancellationToken: ct))).ToHashSet();

        int added = 0, skipped = 0;
        foreach (var a in accounts)
        {
            if (!roles.TryGetValue(a.Role, out var roleId)) throw new InvalidOperationException($"Account '{a.Login}': role '{a.Role}' is not seeded (migration 0003).");
            if (!ranks.Contains(a.Rank)) throw new InvalidOperationException($"Account '{a.Login}': rank '{a.Rank}' is not seeded.");
            var login = a.Login.ToLowerInvariant();
            var exists = await c.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM app_users WHERE login = @login OR display_name = @name", new { login, name = a.DisplayName }, tx, cancellationToken: ct));
            if (exists > 0) { skipped++; log.LogInformation("Skip {Login}: already imported.", login); continue; }

            await c.ExecuteAsync(new CommandDefinition("""
                INSERT INTO app_users (login, display_name, role_id, `rank`, password_hash, must_change_password, is_active, security_stamp)
                VALUES (@login, @name, @roleId, @rank, @hash, 1, 1, @stamp)
                """, new { login, name = a.DisplayName, roleId, rank = a.Rank, hash = a.PasswordHash, stamp = Stamps.New() }, tx, cancellationToken: ct));
            await c.ExecuteAsync(new CommandDefinition("""
                INSERT INTO app_admin_audit (actor_user_id, actor_name, action, target_type, target_name, after_json)
                VALUES (NULL, 'import-users', 'user.import', 'user', @name, JSON_OBJECT('login', @login, 'role', @role, 'rank', @rank))
                """, new { name = a.DisplayName, login, role = a.Role, rank = a.Rank }, tx, cancellationToken: ct));
            added++;
            log.LogInformation("Imported {Login} ({Role}/{Rank}).", login, a.Role, a.Rank);   // never log hashes
        }
        await tx.CommitAsync(ct);
        log.LogInformation("Import finished: {Added} added, {Skipped} skipped.", added, skipped);
        return 0;
    }
}
