using Goldata.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Goldata.Api.Security;

/// <summary>RFC 9457 Problem Details with <c>code</c> + zh-CN <c>title</c> (002-spec §6).</summary>
public static class Problems
{
    public static Task WriteAsync(HttpContext http, int status, string code, string title, object? detail = null)
    {
        http.Response.StatusCode = status;
        var problem = new ProblemDetails { Status = status, Title = title, Type = $"https://goldata/errors/{code}" };
        problem.Extensions["code"] = code;
        if (detail is not null) problem.Extensions["data"] = detail;
        problem.Extensions["requestId"] = http.TraceIdentifier;
        return http.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
    }

    public static IApplicationBuilder UseGoldataProblems(this IApplicationBuilder app) =>
        app.UseExceptionHandler(b => b.Run(async http =>
        {
            var ex = http.Features.Get<IExceptionHandlerFeature>()?.Error;
            var log = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Goldata.Errors");
            switch (ex)
            {
                case AppError e:
                    if (e.Status >= 500) log.LogError(e, "AppError {Code}", e.Code);
                    await WriteAsync(http, e.Status, e.Code, e.Title, e.Detail);
                    break;
                case BadHttpRequestException b:
                    await WriteAsync(http, b.StatusCode, "BAD_REQUEST", b.StatusCode == 413 ? "文件太大" : "请求格式不正确");
                    break;
                case MySqlConnector.MySqlException m when m.ErrorCode is MySqlConnector.MySqlErrorCode.AccessDenied
                        or MySqlConnector.MySqlErrorCode.HostNotPrivileged or MySqlConnector.MySqlErrorCode.UnableToConnectToHost
                        or MySqlConnector.MySqlErrorCode.UnknownDatabase or MySqlConnector.MySqlErrorCode.DatabaseAccessDenied:
                    // Setup problem, not a bug: say so clearly (details in the log and /health/ready).
                    log.LogError("Database connection failed: {Code} {Message}", (int)m.ErrorCode, m.Message);
                    await WriteAsync(http, 503, "DB_UNAVAILABLE", "数据库连接失败，请联系管理员（详情见 /health/ready）");
                    break;
                case OperationCanceledException when http.RequestAborted.IsCancellationRequested:
                    break;
                default:
                    log.LogError(ex, "Unhandled error");
                    await WriteAsync(http, 500, "SERVER_ERROR", "服务器错误，请联系管理员（请提供请求编号）");
                    break;
            }
        }));
}
