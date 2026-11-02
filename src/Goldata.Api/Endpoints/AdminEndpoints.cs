using Goldata.Api.Security;
using Goldata.Application.Permissions;
using Goldata.Application.Security;
using Goldata.Domain;
using Goldata.Infrastructure.Identity;
using Goldata.Infrastructure.Legacy;

namespace Goldata.Api.Endpoints;

public sealed record UpdateUserBody(int RoleId, string Rank, string? DisplayName, string? Login, int RowVersion);
public sealed record CreateRoleBody(string Name, string? Description, bool IsReadOnly, int? CopyFromRoleId);
public sealed record UpdateRoleBody(string? Description, bool IsReadOnly, int RowVersion);
public sealed record MatrixBody(Dictionary<string, List<string>> Layers, int RowVersion);
public sealed record OverridesBody(List<UserOverride> Overrides);
public sealed record RankBody(string Name);

/// <summary>
/// 设置 ▸ 用户与权限 (002-spec §6.3). Cookie session only: <c>/mcp</c> bearer tokens never reach these routes,
/// and there are no admin MCP tools (REQ-ADM-14).
/// </summary>
public static class AdminEndpoints
{
    public static void MapAdmin(this IEndpointRouteBuilder api)
    {
        var users = api.MapGroup("/admin/users").RequireAuthorization("web").AddEndpointFilter(Require(PermissionCatalog.UsersAdmin));
        var roles = api.MapGroup("/admin").RequireAuthorization("web").AddEndpointFilter(Require(PermissionCatalog.RolesAdmin));
        var any = api.MapGroup("/admin").RequireAuthorization("web").AddEndpointFilter(RequireAny(PermissionCatalog.UsersAdmin, PermissionCatalog.RolesAdmin));

        static string? Ip(HttpContext h) => h.Connection.RemoteIpAddress?.ToString();

        users.MapGet("", async (string? q, int? roleId, string? rank, string? status, AdminService a, CancellationToken ct) =>
            Results.Ok(await a.ListUsersAsync(q, roleId, rank, status, ct)));
        users.MapPost("", async (CreateUserRequest r, HttpContext h, ICurrentUser me, AdminService a, CancellationToken ct) =>
        {
            var (user, password) = await a.CreateUserAsync(r, me, Ip(h), ct);
            return Results.Ok(new { user, initialPassword = password });
        });
        users.MapGet("/{id:int}", async (int id, AdminService a, CancellationToken ct) => Results.Ok(await a.GetUserAsync(id, ct)));
        users.MapPut("/{id:int}", async (int id, UpdateUserBody b, HttpContext h, ICurrentUser me, AdminService a, CancellationToken ct) =>
            Results.Ok(await a.UpdateUserAsync(id, new UpdateUserRequest(b.RoleId, b.Rank, b.DisplayName, b.Login), b.RowVersion, me, Ip(h), ct)));
        users.MapDelete("/{id:int}", async (int id, bool? removeFromStaffLookup, HttpContext h, ICurrentUser me, AdminService a, CancellationToken ct) =>
        {
            await a.DeleteUserAsync(id, removeFromStaffLookup == true, me, Ip(h), ct);
            return Results.NoContent();
        });
        users.MapGet("/{id:int}/delete-check", async (int id, AdminService a, CancellationToken ct) => Results.Ok(await a.DeleteCheckAsync(id, ct)));
        users.MapPost("/{id:int}/disable", async (int id, HttpContext h, ICurrentUser me, AdminService a, CancellationToken ct) =>
            Results.Ok(await a.DisableAsync(id, me, Ip(h), ct)));
        users.MapPost("/{id:int}/enable", async (int id, HttpContext h, ICurrentUser me, AdminService a, CancellationToken ct) =>
            Results.Ok(await a.EnableAsync(id, me, Ip(h), ct)));
        users.MapPost("/{id:int}/unlock", async (int id, HttpContext h, ICurrentUser me, AdminService a, CancellationToken ct) =>
            Results.Ok(await a.UnlockAsync(id, me, Ip(h), ct)));
        users.MapPost("/{id:int}/reset-password", async (int id, HttpContext h, ICurrentUser me, AdminService a, CancellationToken ct) =>
            Results.Ok(new { password = await a.ResetPasswordAsync(id, me, Ip(h), ct) }));
        users.MapGet("/{id:int}/overrides", async (int id, AdminService a, CancellationToken ct) => Results.Ok(await a.GetOverridesAsync(id, ct)));
        users.MapPut("/{id:int}/overrides", async (int id, OverridesBody b, HttpContext h, ICurrentUser me, AdminService a, CancellationToken ct) =>
        {
            await a.PutOverridesAsync(id, b.Overrides, me, Ip(h), ct);
            return Results.NoContent();
        });
        users.MapGet("/{id:int}/effective-permissions", async (int id, AdminService a, CancellationToken ct) => Results.Ok(await a.EffectiveAsync(id, ct)));
        users.MapGet("/{id:int}/tokens", async (int id, AdminService a, CancellationToken ct) => Results.Ok(await a.UserTokensAsync(id, ct)));
        users.MapDelete("/{id:int}/tokens", async (int id, HttpContext h, ICurrentUser me, AdminService a, CancellationToken ct) =>
        {
            await a.RevokeUserTokenAsync(id, null, me, Ip(h), ct);
            return Results.NoContent();
        });
        users.MapDelete("/{id:int}/tokens/{tokenId:int}", async (int id, int tokenId, HttpContext h, ICurrentUser me, AdminService a, CancellationToken ct) =>
        {
            await a.RevokeUserTokenAsync(id, tokenId, me, Ip(h), ct);
            return Results.NoContent();
        });
        users.MapGet("/staff-codes", async (LookupService l, CancellationToken ct) => Results.Ok(await l.StaffCodesAsync(ct)));
        users.MapGet("/staff-names", async (LookupService l, CancellationToken ct) => Results.Ok(await l.ActiveStaffAsync(ct)));

        any.MapGet("/roles", async (AdminService a, CancellationToken ct) => Results.Ok(await a.ListRolesAsync(ct)));
        any.MapGet("/ranks", async (AdminService a, CancellationToken ct) =>
            Results.Ok((await a.ListRanksAsync(ct)).Select(r => new { r.Name, r.Sort, r.UserCount })));
        any.MapGet("/permissions", () => Results.Ok(PermissionCatalog.All));
        any.MapGet("/audit", async (string? actor, string? target, string? action, DateTime? from, DateTime? to, AdminService a, CancellationToken ct) =>
            Results.Ok(await a.AuditLogAsync(actor, target, action, from, to, ct)));

        roles.MapPost("/roles", async (CreateRoleBody b, HttpContext h, ICurrentUser me, AdminService a, CancellationToken ct) =>
            Results.Ok(await a.CreateRoleAsync(b.Name, b.Description, b.IsReadOnly, b.CopyFromRoleId, me, Ip(h), ct)));
        roles.MapPut("/roles/{id:int}", async (int id, UpdateRoleBody b, HttpContext h, ICurrentUser me, AdminService a, CancellationToken ct) =>
            Results.Ok(await a.UpdateRoleAsync(id, b.Description, b.IsReadOnly, b.RowVersion, me, Ip(h), ct)));
        roles.MapDelete("/roles/{id:int}", async (int id, HttpContext h, ICurrentUser me, AdminService a, CancellationToken ct) =>
        {
            await a.DeleteRoleAsync(id, me, Ip(h), ct);
            return Results.NoContent();
        });
        roles.MapGet("/roles/{id:int}/permissions", async (int id, AdminService a, CancellationToken ct) => Results.Ok(await a.GetMatrixAsync(id, ct)));
        roles.MapPut("/roles/{id:int}/permissions", async (int id, MatrixBody b, bool? dryRun, HttpContext h, ICurrentUser me, AdminService a, CancellationToken ct) =>
            Results.Ok(await a.PutMatrixAsync(id, b.Layers.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<string>)kv.Value), b.RowVersion,
                dryRun == true, me, Ip(h), ct)));
        roles.MapPost("/roles/{id:int}/reset-default", async (int id, HttpContext h, ICurrentUser me, AdminService a, CancellationToken ct) =>
            Results.Ok(await a.ResetDefaultAsync(id, me, Ip(h), ct)));
        roles.MapPost("/ranks", async (RankBody b, HttpContext h, ICurrentUser me, AdminService a, CancellationToken ct) =>
        {
            await a.CreateRankAsync(b.Name, me, Ip(h), ct);
            return Results.NoContent();
        });
        roles.MapDelete("/ranks/{name}", async (string name, HttpContext h, ICurrentUser me, AdminService a, CancellationToken ct) =>
        {
            await a.DeleteRankAsync(name, me, Ip(h), ct);
            return Results.NoContent();
        });
    }

    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> Require(string permission) =>
        async (ctx, next) =>
        {
            ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUser>().Require(permission);
            return await next(ctx);
        };

    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> RequireAny(params string[] permissions) =>
        async (ctx, next) =>
        {
            var u = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
            if (!permissions.Any(u.Has)) throw AppError.Forbidden();
            return await next(ctx);
        };
}
