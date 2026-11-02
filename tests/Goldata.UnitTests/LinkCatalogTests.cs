using Goldata.Application.Links;
using Goldata.Application.Modules;
using Goldata.Application.Permissions;
using Goldata.Domain;
using Goldata.Infrastructure.Legacy;

namespace Goldata.UnitTests;

public class LinkCatalogTests
{
    [Fact]
    public void All_30_legacy_link_types_are_defined_once() =>
        LinkCatalog.All.Select(l => l.Code).ShouldBeUnique();

    [Fact]
    public void Catalogue_has_the_30_codes_of_spec_3_6()
    {
        LinkCatalog.All.Count.ShouldBe(30);
        LinkCatalog.All.Count(l => l.IsShare).ShouldBe(16);
        LinkCatalog.All.Count(l => !l.IsShare).ShouldBe(14);
    }

    [Fact]
    public void Links_point_at_real_modules_with_a_link_permission()
    {
        foreach (var l in LinkCatalog.All)
        {
            ModuleRegistry.Find(l.FromModule).ShouldNotBeNull(l.Code);
            ModuleRegistry.Find(l.ToModule).ShouldNotBeNull(l.Code);
            PermissionCatalog.IsValid(PermissionCatalog.Code(l.FromModule, "link")).ShouldBeTrue(l.Code);
        }
    }

    [Fact]
    public void Fk_links_write_to_the_from_table()
    {
        foreach (var l in LinkCatalog.All.Where(l => !l.IsShare))
            l.Table.ShouldBe(ModuleRegistry.Get(l.FromModule).Table, l.Code);
    }

    [Fact]
    public void Delivery_links_clear_the_other_project_column()
    {
        LinkCatalog.Find("delivery-project-link")!.ClearColumns.ShouldBe(["project_retail_id"]);
        LinkCatalog.Find("delivery-project_retail-link")!.ClearColumns.ShouldBe(["project_id"]);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("1,234.567", 1234.57)]
    [InlineData("￥50", 50)]
    [InlineData("-10", -10)]
    public void Share_amounts_parse_like_legacy(string raw, double expected) => LinkService.ParseShare(raw).ShouldBe(expected);

    [Fact]
    public void Share_amount_must_be_a_number() => Should.Throw<AppError>(() => LinkService.ParseShare("abc"));
}
