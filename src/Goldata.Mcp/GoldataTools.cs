using System.ComponentModel;
using Goldata.Application.Records;
using Goldata.Application.Security;
using Goldata.Infrastructure.Legacy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Goldata.Mcp;

/// <summary>
/// MCP tools (002-spec §7.2), phase 1 read set. Every call runs as the token's user with exactly that user's
/// permissions (REQ-MCP-4); HR modules are hidden unless <c>mcp.hr.enabled</c> (REQ-MCP-5). No delete tools.
/// </summary>
[McpServerToolType]
public sealed class GoldataTools(IHttpContextAccessor http, RecordService records, TodoService todos, LookupService lookups)
{
    private ICurrentUser User => http.HttpContext?.RequestServices.GetRequiredService<ICurrentUser>()
                                 ?? throw new InvalidOperationException("No HTTP context.");

    [McpServerTool(Name = "list_modules", ReadOnly = true, Title = "列出可访问的模块")]
    [Description("Lists the ERP modules (广泰管理系统) the user can read, with their keys and Chinese field labels. Call this first to learn module keys.")]
    public async Task<object> ListModules(CancellationToken ct) =>
        (await records.MetaAsync(User, ct))
        .Where(m => m.Fields.Count > 0)
        .Select(m => new { m.Key, m.Title, group = m.GroupLabel, m.Workflow, fields = m.Fields.Select(f => new { f.Name, f.Label, f.Kind }) });

    [McpServerTool(Name = "search_records", ReadOnly = true, Title = "搜索记录")]
    [Description("Full-text search across modules (project names, contract numbers, suppliers, invoice numbers, comments...). Returns module key, id and title of each hit.")]
    public async Task<object> SearchRecords(
        [Description("Search text, e.g. a project name, contract number or supplier.")] string query,
        [Description("Optional module keys to restrict the search, e.g. [\"contracts\",\"buy-orders\"].")] string[]? modules = null,
        [Description("Max hits per module (1-20, default 5).")] int limit = 5,
        CancellationToken ct = default) =>
        await records.SearchAsync(query, modules, Math.Clamp(limit, 1, 20), User, ct);

    [McpServerTool(Name = "get_record", ReadOnly = true, Title = "读取记录")]
    [Description("Reads one record with its line items (明细), attachments list and 转交 history. Field names are DB columns; use list_modules for the Chinese labels.")]
    public async Task<object> GetRecord(
        [Description("Module key, e.g. contracts.")] string module,
        [Description("Record id.")] long id,
        CancellationToken ct = default)
    {
        var detail = await records.GetAsync(module, id, User, ct);
        var history = await records.HistoryAsync(module, id, User, ct);
        return new { module, id, detail.Title, detail.Record, items = detail.Items.Take(200), history = history.Take(50) };
    }

    [McpServerTool(Name = "list_records", ReadOnly = true, Title = "列出记录")]
    [Description("Lists records of one module with optional text search, year and closed/void toggle. Returns rows plus server-computed totals over the whole filtered set.")]
    public async Task<object> ListRecords(
        [Description("Module key, e.g. buy-orders.")] string module,
        [Description("Optional search text.")] string? query = null,
        [Description("Optional year filter (on the module's main date).")] int? year = null,
        [Description("Include closed/void records (结案/作废...). Default false, as in the UI.")] bool includeClosed = false,
        [Description("Sort, e.g. -order_date (minus = descending).")] string? sort = null,
        [Description("Max rows (1-200, default 50).")] int limit = 50,
        CancellationToken ct = default)
    {
        var r = await records.ListAsync(module, new ListRequest
        {
            Q = query, Year = year, IncludeClosed = includeClosed, Sort = sort, PageSize = Math.Clamp(limit, 1, 200),
        }, User, ct);
        return new { r.Total, r.Totals, rows = r.Rows };
    }

    [McpServerTool(Name = "get_history", ReadOnly = true, Title = "转交与修改记录")]
    [Description("Returns the 转交 (hand-over) timeline and the change log (update_logs) of one record.")]
    public async Task<object> GetHistory(string module, long id, CancellationToken ct = default) => new
    {
        handovers = await records.HistoryAsync(module, id, User, ct),
        changes = (await records.LogsAsync(module, id, User, ct)).Take(100),
    };

    [McpServerTool(Name = "my_todos", ReadOnly = true, Title = "我的待办")]
    [Description("Records currently waiting on the user (latest 转交 points to them). scope=others lists other people's to-dos if permitted.")]
    public async Task<object> MyTodos([Description("mine (default) or others")] string scope = "mine", CancellationToken ct = default) =>
        scope == "others" ? await todos.OthersAsync(User, ct) : await todos.MineAsync(User, ct);

    [McpServerTool(Name = "list_lookup", ReadOnly = true, Title = "下拉选项")]
    [Description("Values of one dropdown category from lookup_tables (e.g. 合同类别, 公司员工). Omit category to list all categories.")]
    public async Task<object> ListLookup(string? category = null, CancellationToken ct = default)
    {
        _ = User;   // requires an authenticated MCP user
        return category is null
            ? await lookups.CategoriesAsync(ct)
            : (await lookups.ItemsAsync(category, ct)).Select(i => new { i.Id, i.Item, i.ParentId });
    }
}
