using Equibles.CommonStocks.Data.Helpers;
using Equibles.CommonStocks.Data.Models;

namespace Equibles.UnitTests.CommonStocks;

public class EquityListingReferenceMarkTests
{
    [Fact]
    public void AProductTheReferenceDirectoryReleases_StaysMarkedAsOnceListedThere()
    {
        EquityIssuer bank = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "BANK");

        UsEquityDirectory.ReplaceReferenceSymbols(bank, ["NOTE"]);
        UsEquityDirectory.ReplaceReferenceSymbols(bank, []);

        var note = bank
            .Securities.SelectMany(security => security.Listings)
            .Single(listing => listing.Ticker == "NOTE");
        note.IsReferenceListed.Should().BeFalse();
        note.WasReferenceListed.Should().BeTrue();
        bank.Presentation.Listing.WasReferenceListed.Should().BeFalse();
    }
}
