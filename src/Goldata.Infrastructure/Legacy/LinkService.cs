using System.Globalization;
using Dapper;
using Goldata.Application.Links;
using Goldata.Application.Modules;
using Goldata.Application.Permissions;
using Goldata.Application.Records;
using Goldata.Application.Security;
using Goldata.Domain;
using Goldata.Infrastructure.Data;
using MySqlConnector;

namespace Goldata.Infrastructure.Legacy;

public sealed record LinkTypeInfo(string Code, string Title, string FromModule, string ToModule, string OtherModule, string OtherTitle,
    string Kind, string ShareLabel, bool CanEdit);

public sealed record LinkEntry(long LinkId, long OtherId, string OtherTitle, double? Share);

/// <summary>Links of one type for one record. Direction "out" = this record is the legacy "from" side.</summary>
public sealed record LinkGroup(LinkTypeInfo Type, string Direction, IReadOnlyList<LinkEntry> Items, double? Allocated, double? Amount);

public sealed record LinkCandidate(long Id, string Title, string? Status, double? Amount, double? Allocated, bool AlreadyLinked);

/// <summary>
/// Generic link service replacing <c>link-create.php</c>, <c>project-parent-link-create.php</c> and the unlink branches of
/// <c>delete_row.php</c> (REQ-LINK-1/2, 002-spec §3.6). Permission: <c>{from module}:link</c>; every change → update_logs.
/// </summary>
public sealed class LinkService(Db db, RecordService records)
{
    public async Task<IReadOnlyList<LinkGroup>> ForRecordAsync(string key, long id, ICurrentUser user, CancellationToken ct)
    {
        var m = await records.AuthorizeAsync(key, user, "read", ct);
        await using var c = await db.LegacyAsync(ct);
        var amount = await AmountAsync(c, m.Key, id, ct);
        var groups = new List<LinkGroup>();

        foreach (var t in LinkCatalog.All.Where(t => t.FromModule == m.Key))
        {
            if (!CanRead(user, t.ToModule)) continue;
            var other = ModuleRegistry.Get(t.ToModule);
            var info = Info(t, other, user);
            if (t.IsShare)
            {
                var rows = (await c.QueryAsync<(long Id, long? Other, double? Share)>(new CommandDefinition(
                    $"SELECT CAST(id AS SIGNED), CAST({Q(t.ToColumn)} AS SIGNED), share_amount FROM {Q(t.Table)} WHERE {Q(t.FromColumn)} = @id ORDER BY id",
                    new { id }, cancellationToken: ct))).Where(r => r.Other > 0).ToList();
                var titles = await records.TitlesAsync(other, rows.Select(r => r.Other!.Value).Distinct().ToList(), ct);
                groups.Add(new LinkGroup(info, "out",
                    rows.Select(r => new LinkEntry(r.Id, r.Other!.Value, titles.GetValueOrDefault(r.Other.Value, $"#{r.Other}（不存在）"), r.Share)).ToList(),
                    Round(rows.Sum(r => r.Share ?? 0)), amount));
            }
            else
            {
                var target = await c.ExecuteScalarAsync<long?>(new CommandDefinition(
                    $"SELECT CAST({Q(t.ToColumn)} AS SIGNED) FROM {Q(t.Table)} WHERE `id` = @id", new { id }, cancellationToken: ct));
                var items = new List<LinkEntry>();
                if (target is > 0)
                {
                    var titles = await records.TitlesAsync(other, [target.Value], ct);
                    items.Add(new LinkEntry(0, target.Value, titles.GetValueOrDefault(target.Value, $"#{target}（不存在）"), null));
                }
                groups.Add(new LinkGroup(info, "out", items, null, null));
            }
        }

        // Incoming allocation links (e.g. on an order: the payments allocated to it). FK children are in the related panel.
        foreach (var t in LinkCatalog.All.Where(t => t.ToModule == m.Key && t.IsShare))
        {
            if (!CanRead(user, t.FromModule)) continue;
            var other = ModuleRegistry.Get(t.FromModule);
            var rows = (await c.QueryAsync<(long Id, long? Other, double? Share)>(new CommandDefinition(
                $"SELECT CAST(id AS SIGNED), CAST({Q(t.FromColumn)} AS SIGNED), share_amount FROM {Q(t.Table)} WHERE {Q(t.ToColumn)} = @id ORDER BY id",
                new { id }, cancellationToken: ct))).Where(r => r.Other > 0).ToList();
            if (rows.Count == 0) continue;
            var titles = await records.TitlesAsync(other, rows.Select(r => r.Other!.Value).Distinct().ToList(), ct);
            groups.Add(new LinkGroup(Info(t, other, user), "in",
                rows.Select(r => new LinkEntry(r.Id, r.Other!.Value, titles.GetValueOrDefault(r.Other.Value, $"#{r.Other}（不存在）"), r.Share)).ToList(),
                Round(rows.Sum(r => r.Share ?? 0)), amount));
        }
        return groups;
    }

    public async Task<IReadOnlyList<LinkCandidate>> CandidatesAsync(string code, long fromId, string? q, bool includeClosed, ICurrentUser user, CancellationToken ct)
    {
        var t = Type(code);
        Require(user, t);
        var list = await records.ListAsync(t.ToModule, new ListRequest { Q = q, IncludeClosed = includeClosed, PageSize = 50 }, user, ct);
        var ids = list.Rows.Select(r => Convert.ToInt64(r["id"])).Where(i => !(t.Code == "project-parent-link" && i == fromId)).ToList();
        if (ids.Count == 0) return [];

        await using var c = await db.LegacyAsync(ct);
        var allocated = new Dictionary<long, double>();
        var linked = new HashSet<long>();
        if (t.IsShare)
        {
            foreach (var (toId, sum) in await c.QueryAsync<(long To, double Sum)>(new CommandDefinition(
                         $"SELECT CAST({Q(t.ToColumn)} AS SIGNED), COALESCE(SUM(share_amount), 0) FROM {Q(t.Table)} WHERE {Q(t.ToColumn)} IN @ids GROUP BY {Q(t.ToColumn)}",
                         new { ids }, cancellationToken: ct)))
                allocated[toId] = Round(sum) ?? 0;
            linked = (await c.QueryAsync<long>(new CommandDefinition(
                $"SELECT CAST({Q(t.ToColumn)} AS SIGNED) FROM {Q(t.Table)} WHERE {Q(t.FromColumn)} = @fromId", new { fromId }, cancellationToken: ct))).ToHashSet();
        }
        else if (await c.ExecuteScalarAsync<long?>(new CommandDefinition(
                     $"SELECT CAST({Q(t.ToColumn)} AS SIGNED) FROM {Q(t.Table)} WHERE `id` = @fromId", new { fromId }, cancellationToken: ct)) is { } current and > 0)
            linked.Add(current);

        var amountCol = LinkCatalog.AmountColumns.GetValueOrDefault(t.ToModule);
        var to = ModuleRegistry.Get(t.ToModule);
        return list.Rows.Where(r => ids.Contains(Convert.ToInt64(r["id"]))).Select(r =>
        {
            var rid = Convert.ToInt64(r["id"]);
            double? amt = amountCol is not null && r.GetValueOrDefault(amountCol) is { } a ? Convert.ToDouble(a, CultureInfo.InvariantCulture) : null;
            return new LinkCandidate(rid, (string)r["_title"]!, to.StatusColumn is { } sc ? r.GetValueOrDefault(sc)?.ToString() : null,
                amt, t.IsShare ? allocated.GetValueOrDefault(rid) : null, linked.Contains(rid));
        }).ToList();
    }

    public async Task CreateAsync(string code, long fromId, long toId, string? shareAmount, ICurrentUser user, CancellationToken ct)
    {
        var t = Type(code);
        Require(user, t);
        var from = ModuleRegistry.Get(t.FromModule);
        var to = ModuleRegistry.Get(t.ToModule);
        await using var c = await db.LegacyAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        if (!await ExistsAsync(c, tx, from.Table, fromId, ct, forUpdate: true)) throw AppError.NotFound(from.Title);
        if (!await ExistsAsync(c, tx, to.Table, toId, ct)) throw AppError.NotFound(to.Title);

        if (t.IsShare)
        {
            var share = ParseShare(shareAmount);
            var dup = await c.ExecuteScalarAsync<long>(new CommandDefinition(
                $"SELECT COUNT(*) FROM {Q(t.Table)} WHERE {Q(t.FromColumn)} = @fromId AND {Q(t.ToColumn)} = @toId", new { fromId, toId }, tx, cancellationToken: ct));
            if (dup > 0) throw AppError.Conflict("ALREADY_LINKED", $"已经关联了该{to.Title}（如需改金额，请先解除再关联）");
            var values = new Dictionary<string, object?> { [t.FromColumn] = fromId, [t.ToColumn] = toId, ["share_amount"] = share };
            var linkId = await c.ExecuteScalarAsync<long>(new CommandDefinition(
                $"INSERT INTO {Q(t.Table)} ({Q(t.FromColumn)}, {Q(t.ToColumn)}, share_amount) VALUES (@fromId, @toId, @share); SELECT LAST_INSERT_ID();",
                new { fromId, toId, share }, tx, cancellationToken: ct));
            await LegacyAuditWriter.WriteAsync(c, tx, t.Table, linkId, LegacyAuditWriter.Render("INSERT", t.Table, values), user, LegacyAuditWriter.Added, ct);
        }
        else
        {
            if (t.Code == "project-parent-link") await GuardProjectCycleAsync(c, tx, fromId, toId, ct);
            var values = new Dictionary<string, object?> { [t.ToColumn] = toId };
            foreach (var clear in t.ClearColumns ?? []) values[clear] = 0;      // legacy writes "0" to the other project column
            var set = string.Join(", ", values.Keys.Select((k, i) => $"{Q(k)} = @v{i}"));
            var p = new DynamicParameters(new { fromId });
            var i = 0;
            foreach (var v in values.Values) p.Add("v" + i++, v);
            await c.ExecuteAsync(new CommandDefinition($"UPDATE {Q(t.Table)} SET {set} WHERE `id` = @fromId", p, tx, cancellationToken: ct));
            await LegacyAuditWriter.WriteAsync(c, tx, t.Table, fromId, LegacyAuditWriter.Render("UPDATE", t.Table, values, $"id = {fromId}"), user,
                LegacyAuditWriter.Updated, ct);
        }
        await tx.CommitAsync(ct);
    }

    /// <summary>Share links: delete the link row (it must belong to <paramref name="fromId"/>). FK links: set the column NULL.</summary>
    public async Task DeleteAsync(string code, long fromId, long? linkId, ICurrentUser user, CancellationToken ct)
    {
        var t = Type(code);
        Require(user, t);
        await using var c = await db.LegacyAsync(ct);
        await using var tx = await c.BeginAsync(ct);
        if (t.IsShare)
        {
            if (linkId is null) throw AppError.Validation("缺少关联编号");
            var n = await c.ExecuteAsync(new CommandDefinition(
                $"DELETE FROM {Q(t.Table)} WHERE `id` = @linkId AND {Q(t.FromColumn)} = @fromId", new { linkId, fromId }, tx, cancellationToken: ct));
            if (n == 0) throw AppError.NotFound("关联");
            await LegacyAuditWriter.WriteAsync(c, tx, t.Table, linkId.Value, LegacyAuditWriter.Render("DELETE", t.Table, [], $"id = {linkId}"), user,
                LegacyAuditWriter.Deleted, ct);
        }
        else
        {
            var n = await c.ExecuteAsync(new CommandDefinition(
                $"UPDATE {Q(t.Table)} SET {Q(t.ToColumn)} = NULL WHERE `id` = @fromId", new { fromId }, tx, cancellationToken: ct));
            if (n == 0) throw AppError.NotFound();
            await LegacyAuditWriter.WriteAsync(c, tx, t.Table, fromId,
                LegacyAuditWriter.Render("UPDATE", t.Table, [new(t.ToColumn, null)], $"id = {fromId}"), user, LegacyAuditWriter.Updated, ct);
        }
        await tx.CommitAsync(ct);
    }

    // ---------- helpers ----------

    private static LinkType Type(string code) => LinkCatalog.Find(code) ?? throw AppError.NotFound("关联类型");

    private static bool CanRead(ICurrentUser u, string module) => u.Has(PermissionCatalog.Code(module, "read"));

    private static void Require(ICurrentUser user, LinkType t)
    {
        user.Require(PermissionCatalog.Code(t.FromModule, "link"));
        user.Require(PermissionCatalog.Code(t.ToModule, "read"));
    }

    private static LinkTypeInfo Info(LinkType t, ModuleDefinition other, ICurrentUser user) =>
        new(t.Code, t.Title, t.FromModule, t.ToModule, other.Key, other.Title, t.Kind.ToString().ToLowerInvariant(), t.ShareLabel,
            user.Has(PermissionCatalog.Code(t.FromModule, "link")) && user.Has(PermissionCatalog.Code(t.ToModule, "read")));

    public static double ParseShare(string? raw)
    {
        var text = (raw ?? "").Trim().Replace(",", "").Replace("¥", "").Replace("￥", "");
        if (text.Length == 0) return 0;                       // legacy default $share_amount = 0
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) throw AppError.Validation("摊分金额请输入数字");
        if (Math.Abs(d) >= 1_000_000_000_000m) throw AppError.Validation("摊分金额过大");
        return (double)Math.Round(d, 2);
    }

    private async Task<double?> AmountAsync(MySqlConnection c, string moduleKey, long id, CancellationToken ct)
    {
        if (LinkCatalog.AmountColumns.GetValueOrDefault(moduleKey) is not { } col) return null;
        var m = ModuleRegistry.Get(moduleKey);
        return Round(await c.ExecuteScalarAsync<double?>(new CommandDefinition(
            $"SELECT {Q(col)} FROM {Q(m.Table)} WHERE `id` = @id", new { id }, cancellationToken: ct)));
    }

    /// <summary>A project can't become its own ancestor.</summary>
    private static async Task GuardProjectCycleAsync(MySqlConnection c, MySqlTransaction tx, long childId, long parentId, CancellationToken ct)
    {
        var cur = (long?)parentId;
        for (var depth = 0; cur is > 0 && depth < 50; depth++)
        {
            if (cur == childId) throw AppError.Validation("不能把项目关联到它自己或它的子项目下");
            cur = await c.ExecuteScalarAsync<long?>(new CommandDefinition(
                "SELECT CAST(parent_project_id AS SIGNED) FROM projects WHERE `id` = @cur", new { cur }, tx, cancellationToken: ct));
        }
    }

    private static async Task<bool> ExistsAsync(MySqlConnection c, MySqlTransaction tx, string table, long id, CancellationToken ct, bool forUpdate = false) =>
        await c.ExecuteScalarAsync<long?>(new CommandDefinition(
            $"SELECT CAST(`id` AS SIGNED) FROM {Q(table)} WHERE `id` = @id{(forUpdate ? " FOR UPDATE" : "")}", new { id }, tx, cancellationToken: ct)) is not null;

    private static double? Round(double? v) => v is null ? null : Math.Round(v.Value, 2);

    private static string Q(string identifier) => ListQueryBuilder.Q(identifier);
}
