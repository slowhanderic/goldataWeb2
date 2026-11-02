using Goldata.Api.Security;
using Goldata.Application.Modules;
using Goldata.Application.Permissions;
using Goldata.Domain;
using Goldata.Infrastructure;
using Goldata.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Goldata.Api.Endpoints;

public sealed record LoginRequest(string Login, string Password);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record CreateTokenRequest(string? Name, string[] Scopes, int Days);

public sealed record MeResponse(
    int Id, string Login, string DisplayName, string Role, string Rank, bool RoleIsReadOnly, bool MustChangePassword,
    IReadOnlyList<string> Permissions, IReadOnlyList<MenuGroupDto> Menu);

public sealed record MenuGroupDto(string Group, string Label, IReadOnlyList<MenuItemDto> Items);
public sealed record MenuItemDto(string Key, string Title, bool Generic);

public static class AuthEndpoints
{
    public static void MapAuth(this IEndpointRouteBuilder api)
    {
        var auth = api.MapGroup("/auth");

        // The SPA calls this on start, after login/logout, and when a request fails with code CSRF.
        auth.MapGet("/csrf", (HttpContext http, IAntiforgery af) =>
        {
            var t = af.GetAndStoreTokens(http);
            return Results.Ok(new { token = t.RequestToken, headerName = t.HeaderName });
        });

        auth.MapPost("/login", async (LoginRequest r, HttpContext http, AuthService authService, IAntiforgery af) =>
        {
            var result = await authService.LoginAsync(r.Login, r.Password, http.Connection.RemoteIpAddress?.ToString(), "web", http.RequestAborted);
            if (result.User is null)
                throw result.FailureReason == "locked"
                    ? new AppError(401, "LOCKED", "登入失败次数过多，账户已暂时锁定，请稍后再试或联系管理员")
                    : new AppError(401, "LOGIN_FAILED", "登入名或密码错误");

            var principal = AuthSetup.Principal(result.User);
            await http.SignInAsync(AuthSetup.CookieScheme, principal);
            http.User = principal;                              // so the new antiforgery token is bound to the signed-in user
            http.Items[HttpCurrentUser.ItemKey] = result.User;
            var t = af.GetAndStoreTokens(http);
            return Results.Ok(new { me = ToMe(result.User), csrf = t.RequestToken });
        }).RequireRateLimiting("login");

        auth.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(AuthSetup.CookieScheme);
            return Results.NoContent();
        });

        auth.MapPost("/change-password", async (ChangePasswordRequest r, HttpContext http, HttpCurrentUser me, AuthService authService,
            UserContextService users, IAntiforgery af) =>
        {
            if (!me.IsAuthenticated) throw new AppError(401, "UNAUTHENTICATED", "请先登入");
            await authService.ChangePasswordAsync(me.UserId, r.CurrentPassword, r.NewPassword, http.RequestAborted);
            // The stamp changed: sign in again with the new one (other sessions are now invalid).
            var u = await users.GetAsync(me.UserId, http.RequestAborted) ?? throw AppError.NotFound("用户");
            var principal = AuthSetup.Principal(u);
            await http.SignInAsync(AuthSetup.CookieScheme, principal);
            http.User = principal;
            http.Items[HttpCurrentUser.ItemKey] = u;
            var t = af.GetAndStoreTokens(http);
            return Results.Ok(new { me = ToMe(u), csrf = t.RequestToken });
        }).RequireAuthorization("web").RequireRateLimiting("login");

        var me = api.MapGroup("/me").RequireAuthorization("web");
        me.MapGet("", (HttpCurrentUser u) => Results.Ok(ToMe(u.Context!)));

        me.MapGet("/tokens", async (HttpCurrentUser u, TokenService tokens, CancellationToken ct) =>
            Results.Ok(await tokens.ListAsync(u.UserId, ct)));
        me.MapPost("/tokens", async (CreateTokenRequest r, HttpCurrentUser u, TokenService tokens, IOptions<AuthOptions> o, CancellationToken ct) =>
        {
            if (!u.Context!.Permissions.Contains(PermissionCatalog.McpRead)) throw AppError.Forbidden();
            var scopes = r.Scopes.Distinct().ToList();
            if (scopes.Contains("mcp:write") && !u.Context.Permissions.Contains(PermissionCatalog.McpWrite)) throw AppError.Forbidden();
            if (!scopes.Contains("mcp:read")) scopes.Add("mcp:read");
            var (info, token) = await tokens.CreateAsync(u.UserId, r.Name, scopes, r.Days, ct);
            var baseUrl = o.Value.PublicBaseUrl.TrimEnd('/');
            return Results.Ok(new { info, token, mcpUrl = $"{baseUrl}/mcp" });
        });
        me.MapDelete("/tokens/{id:int}", async (int id, HttpCurrentUser u, TokenService tokens, CancellationToken ct) =>
            await tokens.RevokeAsync(u.UserId, id, ct) ? Results.NoContent() : Results.NotFound());
    }

    public static MeResponse ToMe(UserContext u)
    {
        var menu = ModuleRegistry.All
            .Where(m => u.Permissions.Contains(PermissionCatalog.Code(m.Key, "read")))
            .GroupBy(m => m.Group)
            .OrderBy(g => g.Key)
            .Select(g => new MenuGroupDto(g.Key.ToString(), MenuGroups.Label(g.Key), g.Select(m => new MenuItemDto(m.Key, m.Title, m.Generic)).ToList()))
            .ToList();
        return new MeResponse(u.UserId, u.Login, u.DisplayName, u.Role, u.Rank, u.RoleIsReadOnly, u.MustChangePassword,
            u.Permissions.Order(StringComparer.Ordinal).ToList(), menu);
    }
}
