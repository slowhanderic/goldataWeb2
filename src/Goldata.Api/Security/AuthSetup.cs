using System.Security.Claims;
using System.Text.Encodings.Web;
using Goldata.Infrastructure;
using Goldata.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Goldata.Api.Security;

public static class AuthSetup
{
    public const string CookieScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    public const string TokenScheme = "GoldataToken";
    public const string UserIdClaim = "uid";
    public const string StampClaim = "stamp";

    public static IServiceCollection AddGoldataAuth(this IServiceCollection services, IConfiguration config)
    {
        var keysPath = config["DataProtection:KeysPath"];
        var dp = services.AddDataProtection().SetApplicationName("goldata-web2");
        if (!string.IsNullOrEmpty(keysPath)) dp.PersistKeysToFileSystem(new DirectoryInfo(keysPath));

        var idleMinutes = config.GetValue("Auth:SessionIdleMinutes", 480);
        services.AddAuthentication(o =>
            {
                o.DefaultScheme = CookieScheme;
                o.DefaultChallengeScheme = CookieScheme;
            })
            .AddCookie(CookieScheme, o =>
            {
                o.Cookie.Name = "goldata.session";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;   // HTTPS via the DSM reverse proxy + ForwardedHeaders
                o.SlidingExpiration = true;
                o.ExpireTimeSpan = TimeSpan.FromMinutes(idleMinutes);
                // An API: answer 401/403 instead of redirecting to a login page.
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
                o.Events.OnValidatePrincipal = ValidateCookieAsync;
            })
            .AddScheme<AuthenticationSchemeOptions, TokenAuthHandler>(TokenScheme, null);

        services.AddAuthorization(o =>
        {
            o.AddPolicy("web", p => p.AddAuthenticationSchemes(CookieScheme).RequireAuthenticatedUser());
            o.AddPolicy("mcp", p => p.AddAuthenticationSchemes(TokenScheme).RequireAuthenticatedUser());
        });

        services.AddAntiforgery(o =>
        {
            o.HeaderName = "X-XSRF-TOKEN";
            o.Cookie.Name = "goldata.af";
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        services.AddHttpContextAccessor();
        services.AddScoped<HttpCurrentUser>();
        services.AddScoped<Application.Security.ICurrentUser>(sp => sp.GetRequiredService<HttpCurrentUser>());
        return services;
    }

    public static ClaimsPrincipal Principal(UserContext u) => new(new ClaimsIdentity(
    [
        new Claim(UserIdClaim, u.UserId.ToString()),
        new Claim(StampClaim, u.SecurityStamp),
        new Claim(ClaimTypes.Name, u.Login),
    ], CookieScheme));

    /// <summary>Every request re-checks that the user is active and the security stamp is unchanged (002-spec §5.3).</summary>
    private static async Task ValidateCookieAsync(CookieValidatePrincipalContext ctx)
    {
        var users = ctx.HttpContext.RequestServices.GetRequiredService<UserContextService>();
        var uid = ctx.Principal?.FindFirst(UserIdClaim)?.Value;
        var stamp = ctx.Principal?.FindFirst(StampClaim)?.Value;
        var u = int.TryParse(uid, out var id) && stamp is not null ? await users.GetValidAsync(id, stamp, ctx.HttpContext.RequestAborted) : null;
        if (u is null)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieScheme);
            return;
        }
        ctx.HttpContext.Items[HttpCurrentUser.ItemKey] = u;
    }
}

/// <summary>Bearer personal access tokens, accepted on <c>/mcp</c> only (REST uses the cookie session).</summary>
public sealed class TokenAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder,
    TokenService tokens, UserContextService users) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthenticateResult.NoResult();
        var token = header["Bearer ".Length..].Trim();
        var valid = await tokens.ValidateAsync(token, Context.RequestAborted);
        if (valid is null) return AuthenticateResult.Fail("invalid token");
        var u = await users.GetAsync(valid.UserId, Context.RequestAborted);
        if (u is null || !u.IsActive || u.MustChangePassword) return AuthenticateResult.Fail("user inactive");

        Context.Items[HttpCurrentUser.ItemKey] = u;
        Context.Items[HttpCurrentUser.ScopesKey] = valid.Scopes;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AuthSetup.UserIdClaim, u.UserId.ToString()), new Claim(ClaimTypes.Name, u.Login)], Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}

/// <summary>
/// API guards: CSRF (antiforgery header on every unsafe cookie request) and the forced password change
/// (only auth endpoints and /me work until the user has changed the initial password, REQ-CMP-7).
/// </summary>
public sealed class ApiGuardMiddleware(RequestDelegate next)
{
    private static readonly string[] AllowedWhileMustChange =
        ["/api/v1/auth/", "/api/v1/me"];

    public async Task InvokeAsync(HttpContext http, IAntiforgery antiforgery)
    {
        var path = http.Request.Path.Value ?? "";
        if (path.StartsWith("/api/", StringComparison.Ordinal))
        {
            var unsafeMethod = !(HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method) || HttpMethods.IsOptions(http.Request.Method));
            if (unsafeMethod && !await antiforgery.IsRequestValidAsync(http))
            {
                await Problems.WriteAsync(http, 400, "CSRF", "页面已过期，请刷新后重试");
                return;
            }
            if (http.Items[HttpCurrentUser.ItemKey] is UserContext { MustChangePassword: true }
                && !AllowedWhileMustChange.Any(p => path.StartsWith(p, StringComparison.Ordinal)))
            {
                await Problems.WriteAsync(http, 403, "MUST_CHANGE_PASSWORD", "请先修改初始密码");
                return;
            }
        }
        await next(http);
    }
}
