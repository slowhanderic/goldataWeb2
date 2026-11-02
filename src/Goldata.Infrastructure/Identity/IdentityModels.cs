namespace Goldata.Infrastructure.Identity;

/// <summary>Row of <c>app_users</c> joined with its role.</summary>
public sealed record AppUser
{
    public int Id { get; init; }
    public string Login { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public int RoleId { get; init; }
    public string RoleName { get; init; } = "";
    public bool RoleIsReadOnly { get; init; }
    public string Rank { get; init; } = "";
    public string PasswordHash { get; init; } = "";
    public bool MustChangePassword { get; init; }
    public bool IsActive { get; init; }
    public int FailedLogins { get; init; }
    public DateTime? LockedUntil { get; init; }
    public string SecurityStamp { get; init; } = "";
    public int RowVersion { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastLoginAt { get; init; }

    public bool IsLocked(DateTime nowLocal) => LockedUntil is { } t && t > nowLocal;

    public const string SelectSql = """
        SELECT u.id, u.login, u.display_name AS DisplayName, u.role_id AS RoleId, r.name AS RoleName,
               r.is_read_only AS RoleIsReadOnly, u.`rank`, u.password_hash AS PasswordHash,
               u.must_change_password AS MustChangePassword, u.is_active AS IsActive, u.failed_logins AS FailedLogins,
               u.locked_until AS LockedUntil, u.security_stamp AS SecurityStamp, u.row_version AS RowVersion,
               u.created_at AS CreatedAt, u.last_login_at AS LastLoginAt
        FROM app_users u JOIN app_roles r ON r.id = u.role_id
        """;
}

// Init properties, not a positional record: Dapper then converts column types (COUNT(*) is BIGINT, a literal 0 is INT),
// whereas a positional constructor must match every column type exactly.
public sealed record AppRole
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string? Description { get; init; }
    public bool IsReadOnly { get; init; }
    public bool IsSeeded { get; init; }
    public int Sort { get; init; }
    public int RowVersion { get; init; }
    public long UserCount { get; init; }
}

/// <summary>Everything a request needs about its user; cached briefly and re-validated against the security stamp.</summary>
public sealed record UserContext(
    int UserId, string Login, string DisplayName, string Role, string Rank, bool RoleIsReadOnly,
    bool MustChangePassword, bool IsActive, string SecurityStamp, IReadOnlySet<string> Permissions);

public static class Stamps
{
    public static string New() => Guid.NewGuid().ToString("N");
}
