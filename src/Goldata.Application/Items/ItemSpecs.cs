namespace Goldata.Application.Items;

public enum ItemFieldKind { Product, Text, Number, Money, Date, Int, Destination }

public sealed record ItemFieldSpec(string Column, ItemFieldKind Kind);

/// <summary>
/// An editable line-item grid (REQ-EDIT-3), with exactly the columns the legacy <c>*-edit.php</c> grid writes
/// (its <c>INSERT … ON DUPLICATE KEY UPDATE</c>). <c>sub_total = qty × price</c> is computed on the server, and
/// <see cref="HeaderTotal"/> (if any) is set to the sum of the line totals, as legacy <c>$_POST['amount'] = $table_total_amount</c>.
/// Item text is stored WITHOUT html encoding — the legacy grids write <c>$_POST[...][$key]</c> raw.
/// </summary>
public sealed record ItemTableSpec(
    string ModuleKey, string Table, string ForeignKey, ItemFieldSpec[] Fields,
    string? SubTotal = null, string? Qty = null, string? Price = null, string? HeaderTotal = null);

public static class ItemSpecs
{
    private static ItemFieldSpec P(string c) => new(c, ItemFieldKind.Product);
    private static ItemFieldSpec T(string c) => new(c, ItemFieldKind.Text);
    private static ItemFieldSpec N(string c) => new(c, ItemFieldKind.Number);
    private static ItemFieldSpec M(string c) => new(c, ItemFieldKind.Money);
    private static ItemFieldSpec D(string c) => new(c, ItemFieldKind.Date);
    private static ItemFieldSpec I(string c) => new(c, ItemFieldKind.Int);

    public static readonly IReadOnlyList<ItemTableSpec> All =
    [
        new("buy-orders", "buy_order_items", "buy_order_id",
            [P("product_id"), T("unit"), N("qty"), M("unit_price"), T("standard"), T("brand"), D("eta"), N("arrived_qty"), T("item_comment")],
            SubTotal: "sub_total", Qty: "qty", Price: "unit_price", HeaderTotal: "amount"),
        new("buy-plans", "buy_plan_items", "buy_plan_id",
            [P("product_id"), T("unit"), N("qty"), M("buy_unit_price"), M("sell_unit_price"), T("standard"), T("brand"), T("supplier"), T("item_comment")]),
        new("buy-requests", "buy_request_items", "buy_request_id",
            [P("product_id"), T("unit"), N("qty"), T("standard"), T("brand"), T("supplier"), D("request_due_date"), I("priority"), T("item_comment")]),
        new("buy-receipts", "buy_receipt_items", "buy_receipt_id",
            [P("product_id"), new("destination_id", ItemFieldKind.Destination), T("unit"), N("qty"), M("unit_price"), T("standard"), T("brand"), T("item_comment")],
            SubTotal: "sub_total", Qty: "qty", Price: "unit_price", HeaderTotal: "amount"),
        new("labor-settlements", "labor_settlement_items", "labor_settlement_id",
            [T("member_name"), T("work_type"), T("level"), T("section"), T("sub_section"), T("unit"), N("qty"), M("unit_price"),
             D("start_date"), D("end_date"), T("item_comment")],
            SubTotal: "sub_total", Qty: "qty", Price: "unit_price"),
        // Not yet: deliveries (stock sign convention, Q5), payrolls (pay formula), quotes (nested requirement rows).
    ];

    private static readonly Dictionary<string, ItemTableSpec> ByModule = All.ToDictionary(s => s.ModuleKey);

    public static ItemTableSpec? For(string moduleKey) => ByModule.GetValueOrDefault(moduleKey);

    /// <summary>Lookup category of the warehouse ids in <c>destination_id</c>.</summary>
    public const string DestinationCategory = "进库目的地";
}
