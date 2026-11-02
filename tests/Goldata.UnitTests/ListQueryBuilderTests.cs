using Goldata.Application.Modules;
using Goldata.Application.Records;
using Goldata.Domain;

namespace Goldata.UnitTests;

public class ListQueryBuilderTests
{
    private static readonly ModuleDefinition Contracts = ModuleRegistry.Get("contracts");

    private static readonly TableSchema Schema = new("contracts",
    [
        new("id", "int", "编号", ColumnKind.Id),
        new("contract_name", "text", "合同名称", ColumnKind.Text),
        new("contract_number", "text", "合同编号", ColumnKind.Text),
        new("contract_date", "date", "合同日期", ColumnKind.Date),
        new("amount", "double", "合同金额", ColumnKind.Money),
        new("settlement_amount", "double", "结算金额", ColumnKind.Money),
        new("project_id", "int", "项目", ColumnKind.Int),
        new("contract_status", "text", "合同情况", ColumnKind.Text),
    ]);

    [Fact]
    public void Default_view_hides_closed_statuses_as_parameters()
    {
        var (page, summary) = ListQueryBuilder.Build(Contracts, Schema, new ListRequest());
        page.Sql.ShouldContain("COALESCE(`contract_status`, '') NOT LIKE @p0");
        page.Sql.ShouldContain("COALESCE(`contract_status`, '') NOT LIKE @p1");
        page.Parameters["p0"].ShouldBe("%结案态%");
        page.Parameters["p1"].ShouldBe("%作废%");
        page.Sql.ShouldContain("ORDER BY `contract_date` DESC, `id` DESC LIMIT @limit OFFSET @offset");
        summary.Sql.ShouldContain("ROUND(SUM(`amount`), 2) AS `sum_amount`");
        summary.Sql.ShouldContain("ROUND(SUM(`settlement_amount`), 2) AS `sum_settlement_amount`");
    }

    [Fact]
    public void IncludeClosed_removes_the_status_filter()
    {
        var (page, _) = ListQueryBuilder.Build(Contracts, Schema, new ListRequest { IncludeClosed = true });
        page.Sql.ShouldNotContain("NOT LIKE");
    }

    [Fact]
    public void Filters_years_and_search_are_parameterised()
    {
        var (page, _) = ListQueryBuilder.Build(Contracts, Schema, new ListRequest
        {
            IncludeClosed = true, Year = 2025, Q = "广州'; DROP TABLE x; --",
            Filters = [new("contract_name", "一期"), new("project_id", "12")],
            DateRanges = [new("contract_date", new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 31))],
        });
        page.Sql.ShouldContain("YEAR(`contract_date`) = @p0");
        page.Sql.ShouldContain("`project_id` = @p2");
        page.Sql.ShouldNotContain("DROP TABLE");
        page.Parameters.Values.ShouldContain("%一期%");
        page.Parameters.Values.ShouldContain(12L);
    }

    [Fact]
    public void Unknown_filter_column_is_rejected() =>
        Should.Throw<AppError>(() => ListQueryBuilder.Build(Contracts, Schema,
            new ListRequest { Filters = [new("password`; --", "x")] }));

    [Fact]
    public void Unknown_sort_keys_are_ignored_not_injected()
    {
        var order = ListQueryBuilder.BuildOrder(Contracts, Schema, "-amount,1;DROP,contract_name");
        order.ShouldBe("`amount` DESC, `contract_name` ASC, `id` DESC");
    }

    [Fact]
    public void Page_size_is_capped()
    {
        var (page, _) = ListQueryBuilder.Build(Contracts, Schema, new ListRequest { PageSize = 100_000, Page = 3 });
        page.Parameters["limit"].ShouldBe(ListQueryBuilder.MaxPageSize);
        page.Parameters["offset"].ShouldBe(2 * ListQueryBuilder.MaxPageSize);
    }

    [Fact]
    public void Identifier_quoting_rejects_anything_but_word_characters() =>
        Should.Throw<ArgumentException>(() => ListQueryBuilder.Q("a`b"));
}
