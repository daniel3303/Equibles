using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceOrderForUpsertTests
{
    [Fact]
    public void OrderForUpsert_UsesOneDeterministicCommonStockLockOrder()
    {
        var firstStock = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondStock = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var holdings = new[]
        {
            new InstitutionalHolding
            {
                CommonStockId = secondStock,
                InstitutionalHolderId = Guid.NewGuid(),
                AccessionNumber = "second",
            },
            new InstitutionalHolding
            {
                CommonStockId = firstStock,
                InstitutionalHolderId = Guid.NewGuid(),
                AccessionNumber = "first",
            },
        };

        var ordered = HoldingsImportService.OrderForUpsert(holdings);

        Assert.Equal([firstStock, secondStock], ordered.Select(holding => holding.CommonStockId));
    }
}
