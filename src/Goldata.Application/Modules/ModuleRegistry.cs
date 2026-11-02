namespace Goldata.Application.Modules;

/// <summary>Menu group (001 §4.1 / 002-spec §8.1). The order is the menu order.</summary>
public enum MenuGroup { Gongzhuang, Jiazhuang, Caigou, Caiwu, Laowu, Renshi, Gongju, Shezhi }

public static class MenuGroups
{
    public static string Label(MenuGroup g) => g switch
    {
        MenuGroup.Gongzhuang => "工装",
        MenuGroup.Jiazhuang => "家装",
        MenuGroup.Caigou => "采购",
        MenuGroup.Caiwu => "财务",
        MenuGroup.Laowu => "劳务",
        MenuGroup.Renshi => "人事",
        MenuGroup.Gongju => "工具",
        MenuGroup.Shezhi => "设置",
        _ => g.ToString(),
    };
}

/// <summary>An attachment slot: the <c>file_path.table_name</c> tag and the folder under <c>pdf_folder/</c>.</summary>
public sealed record FileSlot(string Tag, string Folder, string Label = "附件");

/// <summary>A child item table (明细) linked by a foreign-key column.</summary>
public sealed record ItemTable(string Table, string ForeignKey, string Label = "明细");

/// <summary>
/// Metadata for one module (002-spec §3.2). Columns themselves come from the live schema
/// (<c>information_schema</c>, labels from the Chinese column COMMENTs), so this only holds what the schema can't say.
/// </summary>
public sealed record ModuleDefinition
{
    public required string Key { get; init; }            // URL + permission prefix, e.g. "buy-orders"
    public required string Table { get; init; }          // legacy table, e.g. "buy_orders"
    public required string Title { get; init; }          // zh-CN menu title
    public required MenuGroup Group { get; init; }
    public string[] TitleColumns { get; init; } = [];     // columns that identify a record in lists, to-dos and search
    public string? StatusColumn { get; init; }
    /// <summary>Hidden by default: legacy uses <c>status NOT LIKE '%v%'</c> for each value (REQ-LIST-2).</summary>
    public string[] ClosedValues { get; init; } = [];
    public string? YearColumn { get; init; }
    public string DefaultSort { get; init; } = "-id";
    public string[] Totals { get; init; } = [];
    public FileSlot[] Files { get; init; } = [];
    public ItemTable? Items { get; init; }
    public bool Workflow { get; init; }
    /// <summary>HR data: excluded from MCP unless <c>mcp.hr.enabled</c> (REQ-MCP-5).</summary>
    public bool IsHr { get; init; }
    /// <summary>Columns never returned by the generic API (e.g. raw JSON blobs).</summary>
    public string[] HiddenColumns { get; init; } = [];
    /// <summary>Modules without a generic table (inventory, catalogue) only contribute permissions.</summary>
    public bool Generic { get; init; } = true;
    public string[] Actions { get; init; } = Permissions.ModuleActions.Generic;
}

public static class ModuleRegistry
{
    private static FileSlot Slot(string tag, string folder) => new(tag, folder);

    public static readonly IReadOnlyList<ModuleDefinition> All =
    [
        // 工装
        new() { Key = "projects", Table = "projects", Title = "项目", Group = MenuGroup.Gongzhuang,
            TitleColumns = ["project_name", "project_number"], StatusColumn = "project_status", YearColumn = "start_date",
            DefaultSort = "-start_date", Files = [Slot("pdf_project_file", "projects")] },
        new() { Key = "budgets", Table = "budgets", Title = "预算", Group = MenuGroup.Gongzhuang,
            TitleColumns = ["version", "consulting_firm"], StatusColumn = "status", ClosedValues = ["作废", "核销"],
            YearColumn = "start_date", DefaultSort = "-start_date", Totals = ["proposed_amount", "approved_amount"],
            Files = [Slot("pdf_budget_file", "budgets")] },
        new() { Key = "contracts", Table = "contracts", Title = "合同", Group = MenuGroup.Gongzhuang,
            TitleColumns = ["contract_name", "contract_number"], StatusColumn = "contract_status", ClosedValues = ["结案态", "作废"],
            YearColumn = "contract_date", DefaultSort = "-contract_date", Totals = ["amount", "settlement_amount"],
            Files = [Slot("pdf_contract_file", "contracts")], Workflow = true },
        new() { Key = "receivable-applications", Table = "receivable_applications", Title = "请款", Group = MenuGroup.Gongzhuang,
            TitleColumns = ["type", "client_representative"], StatusColumn = "status",
            ClosedValues = ["作废", "已收款", "未编制", "未递出", "核销"], YearColumn = "startdate", DefaultSort = "-startdate",
            Totals = ["amount", "received_amount"], Files = [Slot("pdf_ra_file", "receivable_applications")], Workflow = true },
        new() { Key = "additionals", Table = "additionals", Title = "签证", Group = MenuGroup.Gongzhuang,
            TitleColumns = ["additional_tag", "consulting_firm"], StatusColumn = "status", ClosedValues = ["作废", "核销"],
            YearColumn = "start_date", DefaultSort = "-start_date", Totals = ["proposed_amount", "approved_amount"],
            Files = [Slot("pdf_additional_file", "additionals")], Workflow = true },
        new() { Key = "documents", Table = "documents", Title = "工程文件", Group = MenuGroup.Gongzhuang,
            TitleColumns = ["type", "client_representative"], StatusColumn = "status", ClosedValues = ["作废", "结案", "结案态"],
            YearColumn = "startdate", DefaultSort = "-startdate", Files = [Slot("pdf_document_file", "documents")], Workflow = true },
        new() { Key = "settlements", Table = "settlements", Title = "结算", Group = MenuGroup.Gongzhuang,
            TitleColumns = ["version", "consulting_firm"], StatusColumn = "status", ClosedValues = ["作废", "收到结算确认单"],
            YearColumn = "start_date", DefaultSort = "-start_date", Totals = ["proposed_amount", "approved_amount"],
            Files = [Slot("pdf_settlement_file", "settlements")], Workflow = true },

        // 家装
        new() { Key = "projects-retail", Table = "projects_retail", Title = "家装项目", Group = MenuGroup.Jiazhuang,
            TitleColumns = ["project_name", "project_number"], StatusColumn = "project_status", ClosedValues = ["作废", "结案态"],
            YearColumn = "initial_date", DefaultSort = "-initial_date",
            Totals = ["quote_amount", "contract_amount", "settlement_amount"],
            Files = [new("pdf_blueprint", "projects_retail/blueprints", "确认图纸"), new("pdf_quote", "projects_retail/quotes", "报价单"),
                     new("pdf_contract", "projects_retail/contracts", "合同")] },
        new() { Key = "quotes-retail", Table = "quotes_retail", Title = "家装报价", Group = MenuGroup.Jiazhuang,
            TitleColumns = ["project_name", "project_number"], StatusColumn = "status", YearColumn = "start_date",
            Totals = ["quote_amount"], Files = [Slot("pdf_quote_retail_file", "quotes_retail")],
            Items = new("quote_retail_items", "quote_retail_id") },

        // 采购
        new() { Key = "suppliers", Table = "suppliers", Title = "供应商名录", Group = MenuGroup.Caigou,
            TitleColumns = ["supplier_name", "brand"], StatusColumn = "supplier_status", ClosedValues = ["停业"],
            YearColumn = "initial_date", DefaultSort = "supplier_name", Files = [Slot("pdf_supplier_file", "suppliers")] },
        new() { Key = "buy-plans", Table = "buy_plans", Title = "采购计划", Group = MenuGroup.Caigou,
            TitleColumns = ["plan_number", "plan_type"], StatusColumn = "status", ClosedValues = ["作废", "已到货"],
            YearColumn = "plan_date", DefaultSort = "-plan_date", Files = [Slot("pdf_bp_file", "buy_plans")],
            Items = new("buy_plan_items", "buy_plan_id"), Workflow = true },
        new() { Key = "buy-requests", Table = "buy_requests", Title = "请购单", Group = MenuGroup.Caigou,
            TitleColumns = ["request_number", "request_type"], StatusColumn = "status", ClosedValues = ["作废", "已到货"],
            YearColumn = "request_date", DefaultSort = "-request_date", Files = [Slot("pdf_br_file", "buy_requests")],
            Items = new("buy_request_items", "buy_request_id"), Workflow = true },
        new() { Key = "buy-orders", Table = "buy_orders", Title = "订单", Group = MenuGroup.Caigou,
            TitleColumns = ["order_number", "supplier"], StatusColumn = "status",
            ClosedValues = ["5/5", "作废", "已到货/已递出请款", "已到货/无需请款"], YearColumn = "order_date",
            DefaultSort = "-order_date", Totals = ["amount"], Files = [Slot("pdf_bo_file", "buy_orders")],
            Items = new("buy_order_items", "buy_order_id"), Workflow = true },
        new() { Key = "buy-receipts", Table = "buy_receipts", Title = "收货单", Group = MenuGroup.Caigou,
            TitleColumns = ["tag", "supplier"], StatusColumn = "status", ClosedValues = ["作废", "验收合格"],
            YearColumn = "arrived_date", DefaultSort = "-arrived_date", Totals = ["amount"],
            Files = [Slot("pdf_brc_file", "buy_receipts")], Items = new("buy_receipt_items", "buy_receipt_id") },
        new() { Key = "inventory", Table = "buy_receipt_items", Title = "库存", Group = MenuGroup.Caigou,
            Generic = false, Actions = ["read", "export"] },
        new() { Key = "deliveries", Table = "deliveries", Title = "出库单", Group = MenuGroup.Caigou,
            TitleColumns = ["tag", "receiver"], StatusColumn = "status", ClosedValues = ["作废", "验收合格"],
            YearColumn = "deliver_date", DefaultSort = "-deliver_date", Totals = ["amount"],
            Files = [Slot("pdf_delivery_file", "deliveries")], Items = new("buy_receipt_items", "delivery_id") },
        new() { Key = "catalogue", Table = "lookup_tables", Title = "产品分类表", Group = MenuGroup.Caigou,
            Generic = false, Actions = ["read", "create", "update", "delete"] },

        // 财务
        new() { Key = "sales-invoices", Table = "sales_invoices", Title = "销项发票", Group = MenuGroup.Caiwu,
            TitleColumns = ["invoice_number", "invoice_client_name"], StatusColumn = "invoice_status", ClosedValues = ["作废", "核销"],
            YearColumn = "invoice_date", DefaultSort = "-invoice_date", Totals = ["amount"],
            Files = [Slot("pdf_sales_invoices_file", "sales_invoices")] },
        new() { Key = "buy-invoices", Table = "buy_invoices", Title = "进项发票", Group = MenuGroup.Caiwu,
            TitleColumns = ["invoice_number", "supplier"], StatusColumn = "invoice_status", ClosedValues = ["作废", "核销"],
            YearColumn = "invoice_date", DefaultSort = "-invoice_date", Totals = ["amount"],
            Files = [Slot("pdf_buy_invoices_file", "buy_invoices")] },
        new() { Key = "receiveds", Table = "receiveds", Title = "收款", Group = MenuGroup.Caiwu,
            TitleColumns = ["sender", "received_type"], YearColumn = "received_date", DefaultSort = "-received_date",
            Totals = ["amount"], Files = [Slot("pdf_bank_received_record", "receiveds")] },
        new() { Key = "payments", Table = "payments", Title = "付款", Group = MenuGroup.Caiwu,
            TitleColumns = ["recevier", "payment_type"], YearColumn = "payment_date", DefaultSort = "-payment_date",
            Totals = ["amount"], Files = [Slot("pdf_bank_record", "payments")] },

        // 劳务
        new() { Key = "labor-settlements", Table = "labor_settlements", Title = "人工费结算单", Group = MenuGroup.Laowu,
            TitleColumns = ["labor_settlement_tag", "group_leader"], StatusColumn = "status", ClosedValues = ["作废", "已付清"],
            YearColumn = "labor_settlement_date", DefaultSort = "-labor_settlement_date", Totals = ["amount"],
            Files = [Slot("pdf_ls_file", "labor_settlements")], Items = new("labor_settlement_items", "labor_settlement_id") },

        // 人事
        new() { Key = "employees", Table = "employees", Title = "员工档案", Group = MenuGroup.Renshi,
            TitleColumns = ["name", "tag"], StatusColumn = "status", ClosedValues = ["作废", "历史", "离职"],
            DefaultSort = "name", Files = [Slot("pdf_employee_file", "employees")], Workflow = true, IsHr = true },
        new() { Key = "payrolls", Table = "payrolls", Title = "工资发放", Group = MenuGroup.Renshi,
            TitleColumns = ["tag", "salary_type"], StatusColumn = "status", ClosedValues = ["作废", "历史", "离职"],
            YearColumn = "payroll_year_month", DefaultSort = "-payroll_year_month", Totals = ["amount"],
            Files = [Slot("pdf_payroll_file", "payrolls")], Items = new("payroll_items", "payroll_id"), Workflow = true, IsHr = true },

        // 工具
        new() { Key = "cost-estimates", Table = "cost_estimate", Title = "安装成本计算器", Group = MenuGroup.Gongju,
            TitleColumns = ["tag"], DefaultSort = "-time", HiddenColumns = ["postArray"], Actions = ["read", "create", "update", "delete"] },
    ];

    private static readonly Dictionary<string, ModuleDefinition> ByKey = All.ToDictionary(m => m.Key);
    private static readonly Dictionary<string, ModuleDefinition> ByTable =
        All.Where(m => m.Generic).ToDictionary(m => m.Table);
    private static readonly Dictionary<string, (ModuleDefinition Module, FileSlot Slot)> ByFileTag =
        All.SelectMany(m => m.Files.Select(f => (m, f))).ToDictionary(x => x.f.Tag, x => (x.m, x.f));

    public static ModuleDefinition? Find(string key) => ByKey.GetValueOrDefault(key);
    public static ModuleDefinition? FindByTable(string table) => ByTable.GetValueOrDefault(table);
    public static (ModuleDefinition Module, FileSlot Slot)? FindByFileTag(string tag) =>
        ByFileTag.TryGetValue(tag, out var x) ? x : null;

    public static ModuleDefinition Get(string key) =>
        Find(key) ?? throw Domain.AppError.NotFound("模块");
}
