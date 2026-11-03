using Goldata.Application.Items;
using Goldata.Application.Modules;

namespace Goldata.UnitTests;

public class ItemSpecsTests
{
    [Fact]
    public void Every_grid_belongs_to_a_module_with_the_same_item_table()
    {
        foreach (var s in ItemSpecs.All)
        {
            var m = ModuleRegistry.Get(s.ModuleKey);
            m.Items.ShouldNotBeNull(s.ModuleKey);
            m.Items!.Table.ShouldBe(s.Table);
            m.Items.ForeignKey.ShouldBe(s.ForeignKey);
        }
    }

    [Fact]
    public void Computed_and_key_columns_are_never_editable()
    {
        foreach (var s in ItemSpecs.All)
        {
            s.Fields.Select(f => f.Column).ShouldBeUnique(s.Table);
            s.Fields.ShouldNotContain(f => f.Column == "id" || f.Column == s.ForeignKey || f.Column == s.SubTotal, s.Table);
            if (s.SubTotal is not null)
            {
                s.Fields.ShouldContain(f => f.Column == s.Qty, s.Table);
                s.Fields.ShouldContain(f => f.Column == s.Price, s.Table);
            }
        }
    }

    [Fact]
    public void Orders_and_receipts_update_the_header_amount_like_legacy()
    {
        ItemSpecs.For("buy-orders")!.HeaderTotal.ShouldBe("amount");
        ItemSpecs.For("buy-receipts")!.HeaderTotal.ShouldBe("amount");
        ItemSpecs.For("labor-settlements")!.HeaderTotal.ShouldBeNull();
        ItemSpecs.For("deliveries").ShouldBeNull();   // Q5: stock sign convention still open
    }
}
