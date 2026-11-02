using Goldata.Application.Security;
using Goldata.Infrastructure.Identity;

namespace Goldata.Api.Security;

/// <summary>
/// <see cref="ICurrentUser"/> for the current HTTP request. The authentication handlers put the validated
/// <see cref="UserContext"/> into <c>HttpContext.Items</c>; nothing here trusts claims on their own.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string ItemKey = "goldata.user";
    public const string ScopesKey = "goldata.scopes";

    private HttpContext? Http => accessor.HttpContext;
    private UserContext? Ctx => Http?.Items[ItemKey] as UserContext;

    public bool IsAuthenticated => Ctx is not null;
    public int UserId => Ctx?.UserId ?? 0;
    public string Login => Ctx?.Login ?? "";
    public string DisplayName => Ctx?.DisplayName ?? "";
    public string Role => Ctx?.Role ?? "";
    public string Rank => Ctx?.Rank ?? "";
    public Channel Channel => Http?.Request.Path.StartsWithSegments("/mcp") == true ? Channel.Mcp : Channel.Web;
    public IReadOnlySet<string> Scopes => Http?.Items[ScopesKey] as IReadOnlySet<string> ?? EmptySet;

    /// <summary>MCP calls are limited to the user's permissions AND the token scopes (REQ-MCP-4).</summary>
    public IReadOnlySet<string> Permissions
    {
        get
        {
            var perms = Ctx?.Permissions ?? EmptySet;
            if (Channel != Channel.Mcp) return perms;
            if (!Scopes.Contains("mcp:read") || !perms.Contains("mcp:read")) return EmptySet;
            if (Scopes.Contains("mcp:write") && perms.Contains("mcp:write")) return perms;
            return perms.Where(p => Application.Permissions.PermissionCatalog.Find(p) is not { IsWrite: true }).ToHashSet();
        }
    }

    public UserContext? Context => Ctx;

    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>();
}

/// <summary>A fixed identity for CLI jobs (import) where an actor is required.</summary>
public sealed class SystemUser(string name) : ICurrentUser
{
    public bool IsAuthenticated => true;
    public int UserId => 0;
    public string Login => name;
    public string DisplayName => name;
    public string Role => "系统";
    public string Rank => "";
    public Channel Channel => Channel.System;
    public IReadOnlySet<string> Permissions { get; } = new HashSet<string>();
    public IReadOnlySet<string> Scopes { get; } = new HashSet<string>();
}
