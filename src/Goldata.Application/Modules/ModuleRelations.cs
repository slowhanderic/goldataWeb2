namespace Goldata.Application.Modules;

/// <summary>A direct foreign-key column of a module pointing at another module.</summary>
public sealed record RefSpec(string Column, string Module, string Label);

/// <summary>
/// Relations between modules (REQ-EDIT-2, REQ-EDIT-4, REQ-DEL). Many-to-many link tables (buy_order_project, payment_bo, …)
/// are handled by the link service in P1b/P2; here only the direct FK columns.
/// </summary>
public static class ModuleRelations
{
    /// <summary>Direct FK columns: shown as a link on the detail page, and listed as "related" on the target.</summary>
    public static readonly IReadOnlyDictionary<string, RefSpec[]> Refs = new Dictionary<string, RefSpec[]>
    {
        ["contracts"] = [new("project_id", "projects", "工装项目")],
        ["budgets"] = [new("contract_id", "contracts", "合同"), new("project_id", "projects", "工装项目")],
        ["receivable-applications"] = [new("contract_id", "contracts", "合同")],
        ["additionals"] = [new("contract_id", "contracts", "合同")],
        ["documents"] = [new("contract_id", "contracts", "合同")],
        ["settlements"] = [new("contract_id", "contracts", "合同")],
        ["projects"] = [new("parent_project_id", "projects", "上级项目")],
        ["sales-invoices"] = [new("receivable_application_id", "receivable-applications", "请款")],
        ["receiveds"] =
        [
            new("project_id", "projects", "工装项目"), new("project_retail_id", "projects-retail", "家装项目"),
            new("receivable_application_id", "receivable-applications", "请款"), new("sales_invoice_id", "sales-invoices", "销项发票"),
        ],
        ["payments"] =
        [
            new("project_id", "projects", "工装项目"), new("project_retail_id", "projects-retail", "家装项目"),
            new("order_id", "buy-orders", "订单"), new("labor_application_id", "labor-settlements", "人工费结算单"),
            new("buy_invoice_id", "buy-invoices", "进项发票"),
        ],
        ["buy-plans"] = [new("project_id", "projects", "工装项目"), new("project_retail_id", "projects-retail", "家装项目")],
        ["buy-requests"] = [new("project_id", "projects", "工装项目"), new("project_retail_id", "projects-retail", "家装项目")],
        ["buy-orders"] = [new("supplier_id", "suppliers", "供应商")],
        ["deliveries"] = [new("project_id", "projects", "工装项目"), new("project_retail_id", "projects-retail", "家装项目")],
    };

    /// <summary>
    /// Create-from-parent where legacy <c>insert.php</c> simply sets the child's FK column
    /// (<c>insert_table=X&amp;caller_table=Y</c> → <c>$to_link_action = "update"</c>).
    /// Key = child module; value = (parent module, FK column on the child).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Parent, string Column)[]> CreateFrom = new Dictionary<string, (string, string)[]>
    {
        ["contracts"] = [("projects", "project_id")],
        ["budgets"] = [("projects", "project_id")],
        ["receivable-applications"] = [("contracts", "contract_id")],
        ["additionals"] = [("contracts", "contract_id")],
        ["settlements"] = [("contracts", "contract_id")],
    };

    /// <summary>
    /// Column names that reference a module's id anywhere in the legacy schema (FKs and link tables).
    /// A record with any such reference can't be deleted (REQ-DEL, no FKs in the DB to protect it).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> ReferenceColumns = new Dictionary<string, string[]>
    {
        ["projects"] = ["project_id", "parent_project_id"],
        ["projects-retail"] = ["project_retail_id"],
        ["contracts"] = ["contract_id"],
        ["receivable-applications"] = ["receivable_application_id", "ra_id"],
        ["additionals"] = ["additional_id"],
        ["suppliers"] = ["supplier_id"],
        ["buy-plans"] = ["buy_plan_id"],
        ["buy-requests"] = ["buy_request_id"],
        ["buy-orders"] = ["buy_order_id", "order_id"],
        ["buy-receipts"] = ["buy_receipt_id"],
        ["deliveries"] = ["delivery_id"],
        ["sales-invoices"] = ["sales_invoice_id"],
        ["buy-invoices"] = ["buy_invoice_id"],
        ["receiveds"] = ["received_id"],
        ["payments"] = ["payment_id"],
        ["labor-settlements"] = ["labor_settlement_id", "labor_application_id"],
        ["employees"] = ["employee_id"],
        ["payrolls"] = ["payroll_id"],
        ["quotes-retail"] = ["quote_retail_id"],
    };

    /// <summary>Modules whose direct FK points at <paramref name="targetKey"/> (for the related panels).</summary>
    public static IEnumerable<(string Module, RefSpec Ref)> ReferencedBy(string targetKey) =>
        Refs.SelectMany(kv => kv.Value.Where(r => r.Module == targetKey).Select(r => (kv.Key, r)));
}
