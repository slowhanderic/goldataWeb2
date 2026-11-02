using Goldata.Domain;

namespace Goldata.Application.Security;

/// <summary>Where a request came from: affects audit tags and the MCP HR rule.</summary>
public enum Channel { Web, Mcp, System }

/// <summary>The authenticated user of the current request (REST cookie or MCP bearer token).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    int UserId { get; }
    string Login { get; }
    /// <summary>Equals the names stored in goldata (<c>user_name</c>, <c>historys.person_to</c>).</summary>
    string DisplayName { get; }
    string Role { get; }
    string Rank { get; }
    Channel Channel { get; }
    IReadOnlySet<string> Permissions { get; }
    /// <summary>Token scopes for MCP calls (empty for cookie sessions).</summary>
    IReadOnlySet<string> Scopes { get; }
}

public static class CurrentUserExtensions
{
    public static bool Has(this ICurrentUser u, string permission) => u.Permissions.Contains(permission);

    public static void Require(this ICurrentUser u, string permission)
    {
        if (!u.IsAuthenticated) throw new AppError(401, "UNAUTHENTICATED", "请先登入");
        if (!u.Has(permission)) throw AppError.Forbidden();
    }

    /// <summary>Legacy <c>update_logs.user</c> format: <c>{role}-{display name}</c>.</summary>
    public static string AuditUser(this ICurrentUser u) => $"{u.Role}-{u.DisplayName}";
}
