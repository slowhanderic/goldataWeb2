using Goldata.Application.Modules;

namespace Goldata.Application.Permissions;

public static class ModuleActions
{
    public static readonly string[] Generic = ["read", "create", "update", "delete", "link", "workflow", "export", "upload", "file_delete"];

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        ["read"] = "查看", ["create"] = "新增", ["update"] = "修改", ["delete"] = "删除", ["link"] = "关联",
        ["workflow"] = "转交", ["export"] = "导出", ["upload"] = "上传附件", ["file_delete"] = "删除附件",
    };

    /// <summary>Actions that change data: never granted to a read-only role (REQ-ADM-11).</summary>
    public static readonly IReadOnlySet<string> Write = new HashSet<string>
        { "create", "update", "delete", "link", "workflow", "upload", "file_delete" };
}

/// <summary>One grantable permission, e.g. <c>contracts:read</c> or <c>users:admin</c>.</summary>
public sealed record PermissionDef(
    string Code, string Label, string Group, string? Module, string? Action, bool IsWrite, bool IsSensitive);

/// <summary>
/// Every valid permission (002-spec §5.2). The admin page builds its matrix from this list,
/// and the API rejects codes that are not in it.
/// </summary>
public static class PermissionCatalog
{
    public const string TodoAll = "todo:all";
    public const string FeedRecent = "feed:recent";
    public const string ReportsFinance = "reports:finance";
    public const string ReportsSalary = "reports:salary";
    public const string LookupsAdmin = "lookups:admin";
    public const string UsersAdmin = "users:admin";
    public const string RolesAdmin = "roles:admin";
    public const string McpRead = "mcp:read";
    public const string McpWrite = "mcp:write";

    public static string Code(string module, string action) => $"{module}:{action}";

    public static IEnumerable<string> ActionsFor(ModuleDefinition m) =>
        m.Actions.Where(a => (a != "workflow" || m.Workflow) && (a is not ("upload" or "file_delete") || m.Files.Length > 0));

    public static readonly IReadOnlyList<PermissionDef> All = Build();

    private static readonly Dictionary<string, PermissionDef> ByCode = All.ToDictionary(p => p.Code);

    public static bool IsValid(string code) => ByCode.ContainsKey(code);
    public static PermissionDef? Find(string code) => ByCode.GetValueOrDefault(code);

    private static List<PermissionDef> Build()
    {
        var list = new List<PermissionDef>();
        foreach (var m in ModuleRegistry.All)
        {
            foreach (var a in ActionsFor(m))
                list.Add(new(Code(m.Key, a), $"{m.Title}:{ModuleActions.Labels[a]}", MenuGroups.Label(m.Group), m.Key, a,
                    ModuleActions.Write.Contains(a), m.IsHr));
        }
        const string special = "特殊权限";
        list.Add(new(ReportsFinance, "财务分析报告", special, null, null, false, false));
        list.Add(new(ReportsSalary, "工资报告", special, null, null, false, true));
        list.Add(new(TodoAll, "其他人员待办", special, null, null, false, false));
        list.Add(new(FeedRecent, "最近更新", special, null, null, false, false));
        list.Add(new(LookupsAdmin, "下拉选项维护", special, null, null, true, false));
        list.Add(new(UsersAdmin, "用户管理", special, null, null, true, true));
        list.Add(new(RolesAdmin, "角色与权限管理", special, null, null, true, true));
        list.Add(new(McpRead, "MCP 读取", special, null, null, false, false));
        list.Add(new(McpWrite, "MCP 写入", special, null, null, true, true));
        return list;
    }
}
