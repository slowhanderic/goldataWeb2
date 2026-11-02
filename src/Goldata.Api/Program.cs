using System.Threading.RateLimiting;
using Dapper;
using Goldata.Api;
using Goldata.Api.Endpoints;
using Goldata.Api.Security;
using Goldata.Infrastructure;
using Goldata.Infrastructure.Data;
using Goldata.Mcp;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using Serilog;

// All settings come from ONE env file per environment (deploy/env/*.env) — see GoldataEnv.
// Load it before the builder so ASPNETCORE_ENVIRONMENT from the file is honoured.
var envFile = GoldataEnv.LoadDefaultFile();

if (await Cli.TryRunAsync(args) is { } exitCode) return exitCode;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddInMemoryCollection(GoldataEnv.FromProcess());

builder.Host.UseSerilog((ctx, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration).Enrich.FromLogContext().WriteTo.Console();
    if (ctx.Configuration["Serilog:File:Path"] is { Length: > 0 } path)
        cfg.WriteTo.File(path, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);
});

builder.Services.AddGoldataInfrastructure(builder.Configuration);
builder.Services.AddGoldataAuth(builder.Configuration);
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var maxBytes = builder.Configuration.GetValue<long?>("Documents:MaxBytes") ?? 83_886_080;
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxBytes + 1_048_576);

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Connection.RemoteIpAddress?.ToString() ?? "?", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    o.AddPolicy("mcp", http => RateLimitPartition.GetFixedWindowLimiter(
        http.Request.Headers.Authorization.ToString(), _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    foreach (var net in (builder.Configuration["ForwardedHeaders:KnownNetworks"] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        o.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(net.Trim()));
});

builder.Services.AddMcpServer(o => o.ServerInfo = new() { Name = "goldata", Version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0" })
    .WithHttpTransport(o => o.Stateless = true)
    .WithTools<GoldataTools>();

var app = builder.Build();
if (app.Configuration["Db:LegacyDatabase"] is { Length: > 0 } legacyDb
    && new MySqlConnector.MySqlConnectionStringBuilder(app.Configuration.GetConnectionString("App") ?? "").Database is var appDb
    && string.Equals(appDb, legacyDb, StringComparison.OrdinalIgnoreCase))
    app.Logger.LogError("DB_APP_NAME and DB_LEGACY_NAME are both '{Db}'. The app tables belong in a separate database (e.g. goldata_app): fix the env file.", appDb);
app.Logger.LogInformation("GoldataWeb2 {Env} ({AspNetEnv}); settings file: {File}",
    app.Configuration["Goldata:Environment"] ?? "?", app.Environment.EnvironmentName, envFile ?? "container env_file");

app.UseForwardedHeaders();
app.UseGoldataProblems();
app.UseSerilogRequestLogging();
app.Use(async (http, next) =>
{
    http.Response.Headers["X-Request-Id"] = http.TraceIdentifier;
    await next(http);
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ApiGuardMiddleware>();

var api = app.MapGroup("/api/v1");
api.MapAuth();
api.MapRecords();
api.MapAdmin();

app.MapMcp("/mcp").RequireAuthorization("mcp").RequireRateLimiting("mcp");

// Health (002-spec §9.7): live = process; ready = both DBs + pdf_folder writable.
app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", ReadyAsync);
api.MapGet("/health", ReadyAsync);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.Run();
return 0;

static async Task<IResult> ReadyAsync(Db db, IOptions<DocumentOptions> docs, IHostEnvironment env, CancellationToken ct)
{
    var checks = new Dictionary<string, string>();
    // Reason codes help diagnose setup without leaking details; Development adds the full message.
    string Why(Exception e) => e switch
    {
        MySqlConnector.MySqlException m => $"{(int)m.ErrorCode} {m.ErrorCode switch
        {
            MySqlConnector.MySqlErrorCode.AccessDenied => "access denied (user/password, or no grant for this client host)",
            MySqlConnector.MySqlErrorCode.HostNotPrivileged => "this client host is not allowed to connect (grant @'host')",
            MySqlConnector.MySqlErrorCode.UnknownDatabase => "database does not exist (run --migrate for goldata_app)",
            MySqlConnector.MySqlErrorCode.DatabaseAccessDenied => "no rights on this database",
            MySqlConnector.MySqlErrorCode.UnableToConnectToHost => "cannot reach DB_HOST:DB_PORT",
            _ => "MariaDB error",
        }}" + (env.IsDevelopment() ? $" — {m.Message}" : ""),
        _ => e.GetType().Name + (env.IsDevelopment() ? $" — {e.Message}" : ""),
    };
    try { await using var c = await db.LegacyAsync(ct); await c.ExecuteScalarAsync<int>("SELECT 1"); checks["goldata"] = "ok"; }
    catch (Exception e) { checks["goldata"] = Why(e); }
    try { await using var c = await db.AppAsync(ct); await c.ExecuteScalarAsync<int>("SELECT 1"); checks["goldata_app"] = "ok"; }
    catch (Exception e) { checks["goldata_app"] = Why(e); }
    try
    {
        var dir = Path.Combine(docs.Value.RootPath, Goldata.Application.Documents.DocumentPaths.Root);
        var probe = Path.Combine(dir, $".health-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(probe, "", ct);
        File.Delete(probe);
        checks["pdf_folder"] = "ok";
    }
    catch (Exception e) { checks["pdf_folder"] = Why(e); }
    var ok = checks.Values.All(v => v == "ok");
    return Results.Json(new { status = ok ? "ok" : "degraded", checks }, statusCode: ok ? 200 : 503);
}

public partial class Program;
