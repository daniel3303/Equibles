using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class HouseDisclosureClientUnpaddedDateTests
{
    [Theory]
    [InlineData("1/2/2018")]
    [InlineData("01/2/2018")]
    [InlineData("1/02/2018")]
    [InlineData("01/02/2018")]
    public void ParseTransactionLines_UnpaddedDates_PreservesFiledFactsAndRowIndices(string date)
    {
        var result = HouseDisclosureClient.ParseTransactionLinesWithShape(
            [
                "Rejected (BAD) P 2/30/2018 3/1/2018 $1,001 - $15,000",
                $"Company (ABC) s (partial) {date} 1/3/2018 $1,001 - $15,000",
                "Exchange (XYZ) E 1/4/2018 1/5/2018 $1,001 - $15,000",
                "Later (DEF) P 01/06/2018 01/07/2018 $15,001 - $50,000",
            ],
            "Test Member",
            new DateOnly(2018, 3, 2)
        );

        result.RejectedSourceRowCount.Should().Be(1);
        result.PolicySkippedRowCount.Should().Be(1);
        result.Transactions.Select(row => row.SourceRowIndex).Should().Equal(1, 3);
        var recovered = result.Transactions[0];
        recovered.Ticker.Should().Be("ABC");
        recovered.TransactionDate.Should().Be(new DateOnly(2018, 1, 2));
        recovered.TransactionType.Should().Be(CongressTransactionType.Sale);
        recovered.AmountFrom.Should().Be(1_001);
        recovered.AmountTo.Should().Be(15_000);
    }

    [Fact]
    public void ParsePtrPdf_UnpaddedDates_RecoversRowsWithoutCorrectingFiledDates()
    {
        // https://disclosures-clerk.house.gov/public_disc/ptr-pdfs/2018/20008757.pdf
        var bytes = File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Congress",
                "house-ptr-unpadded-dates.pdf"
            )
        );
        var result = HouseDisclosureClient.ParsePtrPdfWithShape(
            bytes,
            "John A. Yarmuth",
            new DateOnly(2018, 1, 18)
        );

        result.RejectedSourceRowCount.Should().Be(0);
        result
            .Transactions.Select(row => row.SourceRowIndex)
            .Should()
            .Equal(Enumerable.Range(0, 8));
        result
            .Transactions.Select(row => row.Ticker)
            .Should()
            .Equal("AGN", "AMZN", "BMY", "XOM", "MSFT", "SHW", "UTX", "DIS");
        result.Transactions[3].TransactionDate.Should().Be(new DateOnly(2017, 12, 7));
        // The sync guard owns rejection of future dates; parsing must preserve the source typo.
        result.Transactions[1].TransactionDate.Should().Be(new DateOnly(2018, 12, 7));
        result.Transactions[2].TransactionDate.Should().Be(new DateOnly(2018, 12, 21));
        result.Transactions[7].TransactionDate.Should().Be(new DateOnly(2018, 12, 7));
    }
}
