using Goldata.Application.Modules;
using Goldata.Application.Records;
using Goldata.Domain;

namespace Goldata.UnitTests;

public class WriteRulesTests
{
    private static readonly ModuleDefinition Contracts = ModuleRegistry.Get("contracts");

    private static readonly TableSchema Schema = new("contracts",
    [
        new("id", "int", "编号", ColumnKind.Id),
        new("contract_name", "text", "合同名称", ColumnKind.Text),
        new("contract_subname", "text", "合同别名", ColumnKind.Text),
        new("contract_number", "text", "合同编号", ColumnKind.Text),
        new("contract_date", "date", "合同日期", ColumnKind.Date),
        new("amount", "double", "合同金额", ColumnKind.Money),
        new("settlement_amount", "double", "结算金额", ColumnKind.Money),
        new("project_id", "int", "项目", ColumnKind.Int),
        new("client_name", "text", "甲方名称", ColumnKind.Text),
        new("secondparty_name", "text", "乙方名称", ColumnKind.Text),
        new("contract_type", "text", "合同类型", ColumnKind.Text),
        new("settlement_type", "text", "结算类型", ColumnKind.Text),
        new("user_name", "text", "录入员", ColumnKind.Text),
        new("contract_status", "text", "合同情况", ColumnKind.Text),
        new("comment", "text", "备注", ColumnKind.Text),
    ]);

    private static Dictionary<string, string?> Valid() => new()
    {
        ["contract_name"] = " 广州A&B项目 \"一期\" ", ["contract_number"] = "HT-001", ["contract_date"] = "2026-11-02",
        ["amount"] = "1,234.50", ["client_name"] = "甲方", ["secondparty_name"] = "广泰",
    };

    [Fact]
    public void Create_writes_legacy_values_for_every_editable_field()
    {
        var v = WriteRules.Normalize(Contracts, Schema, Valid(), isCreate: true);
        v["contract_name"].ShouldBe("广州A&amp;B项目 &quot;一期&quot;");    // trimmed + ENT_COMPAT, like PHP htmlspecialchars
        v["amount"].ShouldBe(1234.5d);
        v["settlement_amount"].ShouldBe(0d);                                 // empty amount → 0 (strict mode rejects '')
        v["contract_date"].ShouldBe("2026-11-02");
        v["comment"].ShouldBe("");
        v.ShouldNotContainKey("contract_status");                            // status changes only through 转交
        v.ShouldNotContainKey("project_id");
    }

    [Fact]
    public void Empty_dates_become_zero_date() =>
        WriteRules.Convert(new FieldSpec("contract_date", FieldInput.Date), Schema.Find("contract_date")!, "").ShouldBe("0000-00-00");

    [Fact]
    public void Required_fields_are_reported_per_field()
    {
        var input = Valid();
        input["contract_name"] = "  ";
        input.Remove("amount");
        var e = Should.Throw<AppError>(() => WriteRules.Normalize(Contracts, Schema, input, isCreate: true));
        var errors = e.Detail.ShouldBeOfType<Dictionary<string, string>>();
        errors.Keys.ShouldBe(["amount", "contract_name"], ignoreOrder: true);
    }

    [Fact]
    public void Non_editable_columns_are_rejected()
    {
        var input = Valid();
        input["contract_status"] = "结案态";
        Should.Throw<AppError>(() => WriteRules.Normalize(Contracts, Schema, input, isCreate: true)).Message.ShouldContain("contract_status");
    }

    [Fact]
    public void Update_only_touches_submitted_fields()
    {
        var v = WriteRules.Normalize(Contracts, Schema, new Dictionary<string, string?> { ["comment"] = "备注" }, isCreate: false);
        v.Keys.ShouldBe(["comment"]);
    }

    [Theory]
    [InlineData("contract_date", "2026/13/40")]
    [InlineData("amount", "abc")]
    [InlineData("comment", "表情😀")]
    public void Bad_values_are_rejected(string column, string value)
    {
        var input = Valid();
        input[column] = value;
        Should.Throw<AppError>(() => WriteRules.Normalize(Contracts, Schema, input, isCreate: true));
    }

    [Fact]
    public void Changes_compare_in_stored_form()
    {
        var stored = new Dictionary<string, object?>
        {
            ["contract_name"] = "A&amp;B", ["amount"] = 100d, ["contract_date"] = new DateTime(2026, 1, 5), ["comment"] = null,
            ["settlement_amount"] = 0d,
        };
        var values = new Dictionary<string, object?>
        {
            ["contract_name"] = "A&amp;B", ["amount"] = 100d, ["contract_date"] = "2026-01-05", ["comment"] = "", ["settlement_amount"] = 5d,
        };
        WriteRules.Changes(values, stored).Keys.ShouldBe(["settlement_amount"]);
    }

    [Fact]
    public void Every_generated_editable_field_is_valid_metadata()
    {
        foreach (var (table, fields) in ModuleFields.Editable)
        {
            fields.Select(f => f.Column).ShouldBeUnique(table);
            fields.Where(f => f.Input is FieldInput.Select or FieldInput.Staff).ShouldAllBe(f => f.Lookup != null, table);
            fields.ShouldAllBe(f => !f.Column.EndsWith("_id") && f.Column != "id", table);
        }
        // Workflow modules: status lookup known, and status not editable directly on contracts (legacy comments it out).
        ModuleFields.StatusLookups["contracts"].ShouldBe("合同情况");
        ModuleFields.Editable["contracts"].ShouldNotContain(f => f.Column == "contract_status");
    }

    [Fact]
    public void Create_from_parent_links_point_at_real_modules() =>
        ModuleRelations.CreateFrom.SelectMany(kv => kv.Value.Select(p => (kv.Key, p.Parent)))
            .ShouldAllBe(x => ModuleRegistry.Find(x.Key) != null && ModuleRegistry.Find(x.Parent) != null);
}
