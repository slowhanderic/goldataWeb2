using Dapper;
using Goldata.Application.Identity;
using Goldata.Domain;
using Goldata.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Goldata.Infrastructure.Identity;

public sealed record LoginResult(UserContext? User, string? FailureReason);

/// <summary>Login, lockout, login audit and password change (002-spec §5.3, REQ-CMP-7).</summary>
public sealed class AuthService(Db db, UserContextService users, IOptions<AuthOptions> options, TimeProvider clock)
{
    private readonly AuthOptions _o = options.Value;

    private DateTime NowLocal => clock.GetLocalNow().DateTime;

    public async Task<LoginResult> LoginAsync(string login, string password, string? ip, string source, CancellationToken ct)
    {
        login = (login ?? "").Trim();
        await using var c = await db.AppAsync(ct);
        var u = await c.QuerySingleOrDefaultAsync<AppUser>(new CommandDefinition(
            AppUser.SelectSql + " WHERE u.login = @login", new { login }, cancellationToken: ct));

        var passwordOk = PasswordHasher.Verify(password, u?.PasswordHash);   // same timing for unknown logins
        string? reason = u switch
        {
            null => "unknown_login",
            { IsActive: false } => "disabled",
            _ when u.IsLocked(NowLocal) => "locked",
            _ when !passwordOk => "bad_password",
            _ => null,
        };

        if (u is not null && reason == "bad_password")
        {
            var failed = u.FailedLogins + 1;
            DateTime? lockUntil = failed >= _o.LockoutAttempts ? NowLocal.AddMinutes(_o.LockoutMinutes) : null;
            await c.ExecuteAsync(new CommandDefinition(
                "UPDATE app_users SET failed_logins = @failed, locked_until = COALESCE(@lockUntil, locked_until) WHERE id = @id",
                new { failed = lockUntil is null ? failed : 0, lockUntil, id = u.Id }, cancellationToken: ct));
        }
        else if (u is not null && reason is null)
        {
            var newHash = PasswordHasher.NeedsRehash(u.PasswordHash) ? PasswordHasher.Hash(password) : u.PasswordHash;
            await c.ExecuteAsync(new CommandDefinition(
                "UPDATE app_users SET failed_logins = 0, locked_until = NULL, last_login_at = NOW(), password_hash = @newHash WHERE id = @id",
                new { newHash, id = u.Id }, cancellationToken: ct));
        }

        await c.ExecuteAsync(new CommandDefinition(
            "INSERT INTO app_login_audit (login, success, source, reason, ip) VALUES (@login, @success, @source, @reason, @ip)",
            new { login = Truncate(login, 64), success = reason is null, source, reason, ip }, cancellationToken: ct));

        if (reason is not null) return new LoginResult(null, reason);
        UserContextService.InvalidateAll();
        return new LoginResult(await users.GetAsync(u!.Id, ct), null);
    }

    /// <summary>Changes the user's own password; rotates the stamp so other sessions and tokens must sign in again.</summary>
    public async Task<string> ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken ct)
    {
        await using var c = await db.AppAsync(ct);
        var u = await c.QuerySingleOrDefaultAsync<AppUser>(new CommandDefinition(
            AppUser.SelectSql + " WHERE u.id = @userId", new { userId }, cancellationToken: ct)) ?? throw AppError.NotFound("用户");
        if (!PasswordHasher.Verify(currentPassword, u.PasswordHash)) throw AppError.Validation("当前密码不正确");
        AdminRules.ValidateNewPassword(newPassword, u.Login);
        if (PasswordHasher.Verify(newPassword, u.PasswordHash)) throw AppError.Validation("新密码不能与当前密码相同");

        var stamp = Stamps.New();
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE app_users SET password_hash = @hash, must_change_password = 0, security_stamp = @stamp,
                   row_version = row_version + 1 WHERE id = @userId
            """, new { hash = PasswordHasher.Hash(newPassword), stamp, userId }, cancellationToken: ct));
        UserContextService.InvalidateAll();
        return stamp;
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
