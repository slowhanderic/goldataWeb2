using Goldata.Api.Security;
using Goldata.Application.Records;
using Goldata.Domain;
using Goldata.Infrastructure;
using Goldata.Infrastructure.Legacy;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Goldata.Api.Endpoints;

public static class RecordEndpoints
{
    public static void MapRecords(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("").RequireAuthorization("web");

        g.MapGet("/modules", async (HttpCurrentUser u, RecordService records, CancellationToken ct) =>
            Results.Ok(await records.MetaAsync(u, ct)));

        // GET /modules/{key}?page=&pageSize=&sort=-date,name&q=&year=&includeClosed=&f.col=v&nf.col=v&from.col=yyyy-MM-dd&to.col=
        g.MapGet("/modules/{key}", async (string key, HttpRequest req, HttpCurrentUser u, RecordService records, CancellationToken ct) =>
            Results.Ok(await records.ListAsync(key, ParseList(req.Query), u, ct)));

        g.MapGet("/modules/{key}/years", async (string key, HttpCurrentUser u, RecordService records, CancellationToken ct) =>
            Results.Ok(await records.YearsAsync(key, u, ct)));

        g.MapGet("/modules/{key}/{id:long}", async (string key, long id, HttpContext http, HttpCurrentUser u, RecordService records, CancellationToken ct) =>
        {
            var detail = await records.GetAsync(key, id, u, ct);
            http.Response.Headers.ETag = detail.ETag;
            return Results.Ok(detail);
        });

        // ---- writes (P1): create / update (If-Match) / delete / 转交 ----
        g.MapPost("/modules/{key}", async (string key, SaveBody body, HttpCurrentUser u, RecordWriteService w, CancellationToken ct) =>
            Results.Ok(await w.CreateAsync(key, body.Values ?? [], body.ParentModule, body.ParentId, u, ct)));

        g.MapPut("/modules/{key}/{id:long}", async (string key, long id, SaveBody body, HttpContext http, HttpCurrentUser u, RecordWriteService w,
            CancellationToken ct) =>
        {
            var r = await w.UpdateAsync(key, id, body.Values ?? [], http.Request.Headers.IfMatch.ToString(), u, ct);
            http.Response.Headers.ETag = r.ETag;
            return Results.Ok(r);
        });

        g.MapDelete("/modules/{key}/{id:long}", async (string key, long id, HttpCurrentUser u, RecordWriteService w, CancellationToken ct) =>
        {
            await w.DeleteAsync(key, id, u, ct);
            return Results.NoContent();
        });

        g.MapGet("/modules/{key}/{id:long}/dependents", async (string key, long id, HttpCurrentUser u, RecordWriteService w, CancellationToken ct) =>
            Results.Ok(await w.DependentsAsync(key, id, u, ct)));

        g.MapGet("/modules/{key}/{id:long}/related", async (string key, long id, HttpCurrentUser u, RecordWriteService w, CancellationToken ct) =>
            Results.Ok(await w.RelatedAsync(key, id, u, ct)));

        g.MapPost("/modules/{key}/{id:long}/handover", async (string key, long id, HandoverBody b, HttpCurrentUser u, RecordWriteService w,
            CancellationToken ct) =>
        {
            await w.HandoverAsync(key, id, b.StatusTo, b.PersonTo, b.Comment, u, ct);
            return Results.NoContent();
        });

        g.MapGet("/modules/{key}/{id:long}/logs", async (string key, long id, HttpCurrentUser u, RecordService records, CancellationToken ct) =>
            Results.Ok(await records.LogsAsync(key, id, u, ct)));

        g.MapGet("/modules/{key}/{id:long}/history", async (string key, long id, HttpCurrentUser u, RecordService records, CancellationToken ct) =>
            Results.Ok(await records.HistoryAsync(key, id, u, ct)));

        g.MapGet("/modules/{key}/{id:long}/files", async (string key, long id, HttpCurrentUser u, FileService files, CancellationToken ct) =>
            Results.Ok(await files.ListAsync(key, id, u, ct)));

        g.MapPost("/modules/{key}/{id:long}/files", async (string key, long id, HttpRequest req, HttpCurrentUser u, FileService files,
            IOptions<DocumentOptions> o, CancellationToken ct) =>
        {
            var size = req.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (size is { IsReadOnly: false }) size.MaxRequestBodySize = o.Value.MaxBytes + 1_048_576;
            if (!req.HasFormContentType) throw AppError.Validation("请使用 multipart/form-data 上传");
            var form = await req.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? throw AppError.Validation("缺少文件");
            var mode = string.Equals(req.Query["conflict"], "rename", StringComparison.OrdinalIgnoreCase) ? ConflictMode.Rename : ConflictMode.Reject;
            await using var stream = file.OpenReadStream();
            var entry = await files.UploadAsync(key, id, req.Query["slot"].FirstOrDefault(), file.FileName, file.Length, stream, mode, u, ct);
            return Results.Ok(entry);
        }).DisableAntiforgery();   // validated by ApiGuardMiddleware (header token), not the form field

        g.MapGet("/files/{fileId:long}", async (long fileId, bool? download, HttpCurrentUser u, FileService files, CancellationToken ct) =>
        {
            var f = await files.OpenAsync(fileId, u, ct);
            var disposition = new ContentDispositionHeaderValue(download == true ? "attachment" : "inline");
            disposition.SetHttpFileName(f.Name);                 // RFC 5987 UTF-8 file name
            return new ContentDispositionResult(
                Results.File(f.FullPath, f.ContentType, enableRangeProcessing: true, lastModified: File.GetLastWriteTimeUtc(f.FullPath)),
                disposition.ToString());
        });

        g.MapDelete("/files/{fileId:long}", async (long fileId, HttpCurrentUser u, FileService files, CancellationToken ct) =>
        {
            await files.DeleteAsync(fileId, u, ct);
            return Results.NoContent();
        });

        g.MapGet("/lookups/categories", async (LookupService lookups, CancellationToken ct) => Results.Ok(await lookups.CategoriesAsync(ct)));
        g.MapGet("/lookups/{category}", async (string category, LookupService lookups, CancellationToken ct) =>
            Results.Ok(await lookups.ItemsAsync(category, ct)));
        g.MapGet("/staff", async (LookupService lookups, CancellationToken ct) => Results.Ok(await lookups.ActiveStaffAsync(ct)));

        g.MapGet("/todos", async (string? scope, HttpCurrentUser u, TodoService todos, CancellationToken ct) =>
            Results.Ok(scope == "others" ? await todos.OthersAsync(u, ct) : await todos.MineAsync(u, ct)));

        g.MapGet("/search", async (string q, string? modules, int? limit, HttpCurrentUser u, RecordService records, CancellationToken ct) =>
            Results.Ok(await records.SearchAsync(q, modules?.Split(',', StringSplitOptions.RemoveEmptyEntries), Math.Clamp(limit ?? 5, 1, 20), u, ct)));
    }

    public sealed record SaveBody(Dictionary<string, string?>? Values, string? ParentModule, long? ParentId);
    public sealed record HandoverBody(string? StatusTo, string? PersonTo, string? Comment);

    public static ListRequest ParseList(IQueryCollection q)
    {
        static int Int(string? s, int def) => int.TryParse(s, out var v) ? v : def;
        var filters = new List<ColumnFilter>();
        var ranges = new Dictionary<string, (DateOnly? From, DateOnly? To)>();
        foreach (var (key, value) in q)
        {
            var v = value.ToString();
            if (key.StartsWith("f.", StringComparison.Ordinal)) filters.Add(new ColumnFilter(key[2..], v));
            else if (key.StartsWith("nf.", StringComparison.Ordinal)) filters.Add(new ColumnFilter(key[3..], v, Not: true));
            else if (key.StartsWith("from.", StringComparison.Ordinal) || key.StartsWith("to.", StringComparison.Ordinal))
            {
                var isFrom = key.StartsWith("from.", StringComparison.Ordinal);
                var col = key[(isFrom ? 5 : 3)..];
                if (!DateOnly.TryParse(v, out var d)) throw AppError.Validation($"日期格式不正确：{v}");
                var cur = ranges.GetValueOrDefault(col);
                ranges[col] = isFrom ? (d, cur.To) : (cur.From, d);
            }
        }
        return new ListRequest
        {
            Page = Int(q["page"], 1),
            PageSize = Int(q["pageSize"], 50),
            Sort = q["sort"].FirstOrDefault(),
            Q = q["q"].FirstOrDefault(),
            Year = int.TryParse(q["year"], out var y) && y > 0 ? y : null,
            IncludeClosed = q["includeClosed"] == "true",
            Filters = filters,
            DateRanges = ranges.Select(kv => new DateRangeFilter(kv.Key, kv.Value.From, kv.Value.To)).ToList(),
        };
    }

    /// <summary>Wraps a file result to set an RFC 5987 Content-Disposition (Chinese file names).</summary>
    private sealed class ContentDispositionResult(IResult inner, string disposition) : IResult
    {
        public Task ExecuteAsync(HttpContext http)
        {
            http.Response.Headers.ContentDisposition = disposition;
            http.Response.Headers.CacheControl = "private, no-store";
            return inner.ExecuteAsync(http);
        }
    }
}
