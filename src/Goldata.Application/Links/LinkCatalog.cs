namespace Goldata.Application.Links;

public enum LinkKind
{
    /// <summary>Row in a link table with <c>share_amount</c> (分摊金额), e.g. payment_bo.</summary>
    Share,
    /// <summary>FK column on the "from" record, e.g. settlements.contract_id. Unlink = NULL (legacy delete_row.php).</summary>
    Fk,
}

/// <summary>
/// One legacy link type of <c>link-create.php</c> / <c>project-parent-link-create.php</c> (REQ-LINK-1, 002-spec §3.6).
/// The legacy <c>tableLinkTableCode</c> is kept as the id so behaviour can be cross-checked.
/// </summary>
public sealed record LinkType(
    string Code, string Title, string FromModule, string ToModule, LinkKind Kind,
    string Table, string FromColumn, string ToColumn, string[]? ClearColumns = null, string ShareLabel = "摊分金额")
{
    /// <summary>For FK links the "from" table itself holds the column; for share links a separate table.</summary>
    public bool IsShare => Kind == LinkKind.Share;
}

public static class LinkCatalog
{
    private static LinkType S(string code, string title, string from, string to, string table, string fromCol, string toCol, string shareLabel = "摊分金额") =>
        new(code, title, from, to, LinkKind.Share, table, fromCol, toCol, ShareLabel: shareLabel);

    private static LinkType F(string code, string title, string from, string to, string table, string fkCol, string[]? clear = null) =>
        new(code, title, from, to, LinkKind.Fk, table, "id", fkCol, clear);

    public static readonly IReadOnlyList<LinkType> All =
    [
        // share-table links (insert into the link table; columns exactly as link-create.php §7.x)
        S("payment-bo-link", "付款 → 订单", "payments", "buy-orders", "payment_bo", "payment_id", "buy_order_id"),
        S("payment-project-link", "付款 → 工装项目", "payments", "projects", "payment_project", "payment_id", "project_id"),
        S("payment-project_retail-link", "付款 → 家装项目", "payments", "projects-retail", "payment_project_retail", "payment_id", "project_retail_id"),
        S("payment-ls-link", "付款 → 人工费结算单", "payments", "labor-settlements", "payment_ls", "payment_id", "labor_settlement_id"),
        S("buy_invoice-payment-link", "进项发票 → 付款", "buy-invoices", "payments", "buy_invoice_payment", "buy_invoice_id", "payment_id"),
        S("received-ra-link", "收款 → 请款", "receiveds", "receivable-applications", "received_receivable_application", "received_id", "receivable_application_id"),
        S("received-project_retail-link", "收款 → 家装项目", "receiveds", "projects-retail", "received_project_retail", "received_id", "project_retail_id"),
        S("bo-project-link", "订单 → 工装项目", "buy-orders", "projects", "buy_order_project", "buy_order_id", "project_id"),
        S("bo-project_retail-link", "订单 → 家装项目", "buy-orders", "projects-retail", "buy_order_project_retail", "buy_order_id", "project_retail_id"),
        S("bo-br-link", "订单 → 请购单", "buy-orders", "buy-requests", "buy_order_request", "buy_order_id", "buy_request_id"),
        S("bo-bp-link", "订单 → 采购计划", "buy-orders", "buy-plans", "buy_order_plan", "buy_order_id", "buy_plan_id"),
        S("receivable_applications-bo-link", "请款 → 订单", "receivable-applications", "buy-orders", "buy_order_ra", "ra_id", "buy_order_id", "摊分(销项)金额"),
        S("brc-bo-link", "收货单 → 订单", "buy-receipts", "buy-orders", "buy_receipt_order", "buy_receipt_id", "buy_order_id"),
        S("ls-project-link", "人工费结算单 → 工装项目", "labor-settlements", "projects", "labor_settlement_project", "labor_settlement_id", "project_id"),
        S("ls-project_retail-link", "人工费结算单 → 家装项目", "labor-settlements", "projects-retail", "labor_settlement_project_retail", "labor_settlement_id", "project_retail_id"),
        S("additional-subproject-link", "签证 → 子项目", "additionals", "projects", "additional_project", "additional_id", "project_id"),

        // FK-column links (UPDATE {from} SET {col} = to; unlink SET NULL)
        F("contract-project-link", "合同 → 工装项目", "contracts", "projects", "contracts", "project_id"),
        F("budget-project-link", "预算 → 工装项目", "budgets", "projects", "budgets", "project_id"),
        F("additional-contract-link", "签证 → 合同", "additionals", "contracts", "additionals", "contract_id"),
        F("settlement-contract-link", "结算 → 合同", "settlements", "contracts", "settlements", "contract_id"),
        F("receivable_applications-contract-link", "请款 → 合同", "receivable-applications", "contracts", "receivable_applications", "contract_id"),
        F("document-contract-link", "工程文件 → 合同", "documents", "contracts", "documents", "contract_id"),
        F("sales_invoice-ra-link", "销项发票 → 请款", "sales-invoices", "receivable-applications", "sales_invoices", "receivable_application_id"),
        F("br-project-link", "请购单 → 工装项目", "buy-requests", "projects", "buy_requests", "project_id"),
        F("br-project_retail-link", "请购单 → 家装项目", "buy-requests", "projects-retail", "buy_requests", "project_retail_id"),
        F("bp-project-link", "采购计划 → 工装项目", "buy-plans", "projects", "buy_plans", "project_id"),
        F("bp-project_retail-link", "采购计划 → 家装项目", "buy-plans", "projects-retail", "buy_plans", "project_retail_id"),
        // 出库单 belongs to ONE project: linking one side resets the other to "0" (link-create.php §7.26/7.27).
        F("delivery-project-link", "出库单 → 工装项目", "deliveries", "projects", "deliveries", "project_id", ["project_retail_id"]),
        F("delivery-project_retail-link", "出库单 → 家装项目", "deliveries", "projects-retail", "deliveries", "project_retail_id", ["project_id"]),
        F("project-parent-link", "子项目 → 父项目", "projects", "projects", "projects", "parent_project_id"),
    ];

    private static readonly Dictionary<string, LinkType> ByCode = All.ToDictionary(l => l.Code);

    public static LinkType? Find(string code) => ByCode.GetValueOrDefault(code);

    /// <summary>The money column used for "allocated vs. unallocated" (REQ-LINK-2).</summary>
    public static readonly IReadOnlyDictionary<string, string> AmountColumns = new Dictionary<string, string>
    {
        ["payments"] = "amount", ["receiveds"] = "amount", ["buy-orders"] = "amount", ["buy-invoices"] = "amount",
        ["sales-invoices"] = "amount", ["receivable-applications"] = "amount", ["labor-settlements"] = "amount",
        ["buy-receipts"] = "amount", ["contracts"] = "amount", ["projects-retail"] = "contract_amount",
        ["additionals"] = "approved_amount", ["deliveries"] = "amount",
    };
}
