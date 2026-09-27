using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class HouseDisclosureClientCrossPageAmountTests
{
    [Fact]
    public void ParsePtrPdf_AmountContinuedAfterPageHeader_PreservesEverySourceRow()
    {
        // https://disclosures-clerk.house.gov/public_disc/ptr-pdfs/2023/20023987.pdf
        // Row 7 continues on page 2.
        var bytes = File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Congress",
                "house-ptr-cross-page-open-amount.pdf"
            )
        );
        var result = HouseDisclosureClient.ParsePtrPdfWithShape(
            bytes,
            "Doris O. Matsui",
            new DateOnly(2023, 11, 20)
        );
        result.RejectedSourceRowCount.Should().Be(0);
        result
            .Transactions.Select(row => row.SourceRowIndex)
            .Should()
            .Equal(Enumerable.Range(0, 9));
        var continued = result.Transactions.Single(row => row.SourceRowIndex == 7);
        continued.AssetName.Should().Be("U.S. Treasury Note due 11/30/2027");
        continued.OwnerType.Should().Be("SP");
        continued.AssetType.Should().Be("GS");
        continued.TransactionType.Should().Be(CongressTransactionType.Purchase);
        continued.TransactionDate.Should().Be(new DateOnly(2023, 11, 6));
        continued.AmountFrom.Should().Be(1_000_000);
        continued.AmountTo.Should().Be(1_000_000);
    }
}
