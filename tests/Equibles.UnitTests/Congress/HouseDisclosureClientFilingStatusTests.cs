using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

public class HouseDisclosureClientFilingStatusTests
{
    private static HouseDisclosureClient.HousePtrParseResult ParseAsset(
        string fileName,
        string member,
        DateOnly filingDate
    ) =>
        HouseDisclosureClient.ParsePtrPdfWithShape(
            File.ReadAllBytes(
                Path.Combine(AppContext.BaseDirectory, "TestAssets", "Congress", fileName)
            ),
            member,
            filingDate
        );

    [Fact]
    public void ParsePtrPdf_CastorOriginal_ReadsNewRowsWithTheFiledYearTypo()
    {
        // https://disclosures-clerk.house.gov/public_disc/ptr-pdfs/2021/20019168.pdf
        var result = ParseAsset(
            "house-ptr-castor-original.pdf",
            "Kathy Castor",
            new DateOnly(2021, 7, 27)
        );

        result
            .Transactions.Select(row => row.FilingStatus)
            .Should()
            .AllBeEquivalentTo(CongressionalTradeFilingStatus.New);
        result.Transactions.Select(row => row.SourceRowIndex).Should().Equal(0, 1, 2);
        result
            .Transactions.Select(row => row.TransactionDate)
            .Should()
            .Equal(new DateOnly(2020, 6, 18), new DateOnly(2020, 6, 25), new DateOnly(2020, 7, 16));
    }

    [Fact]
    public void ParsePtrPdf_CastorAmendment_ReadsEveryRowAsAmended()
    {
        // https://disclosures-clerk.house.gov/public_disc/ptr-pdfs/2021/20019181.pdf
        var result = ParseAsset(
            "house-ptr-castor-amendment.pdf",
            "Kathy Castor",
            new DateOnly(2021, 7, 29)
        );

        result.RejectedSourceRowCount.Should().Be(0);
        result
            .Transactions.Select(row => row.FilingStatus)
            .Should()
            .AllBeEquivalentTo(CongressionalTradeFilingStatus.Amended);
        result
            .Transactions.Select(row => row.TransactionDate)
            .Should()
            .Equal(new DateOnly(2021, 7, 16), new DateOnly(2021, 6, 25), new DateOnly(2021, 6, 18));
        result
            .Transactions.Should()
            .OnlyContain(row => row.Subholding == "Vanguard Joint Retirement");
    }

    [Fact]
    public void ParsePtrPdf_HernAmendment_ReadsAmendedAndDeletedRows()
    {
        // https://disclosures-clerk.house.gov/public_disc/ptr-pdfs/2026/20035196.pdf
        var result = ParseAsset(
            "house-ptr-hern-amendment.pdf",
            "Kevin Hern",
            new DateOnly(2026, 8, 12)
        );

        result.Transactions.Should().HaveCount(2);
        var organon = result.Transactions[0];
        organon.Ticker.Should().Be("OGN");
        organon.FilingStatus.Should().Be(CongressionalTradeFilingStatus.Amended);
        organon.Subholding.Should().Be("Hern Family Revocable Trust");
        organon.SourceRowIndex.Should().Be(0);
        var versant = result.Transactions[1];
        versant.Ticker.Should().Be("VSNT");
        versant.FilingStatus.Should().Be(CongressionalTradeFilingStatus.Deleted);
        versant.SourceRowIndex.Should().Be(1, "a deleted row still takes its source row index");
    }

    [Fact]
    public void ParsePtrPdf_HernOriginal_KeepsTheRowIndicesStoredBeforeStatusWasRead()
    {
        // https://disclosures-clerk.house.gov/public_disc/ptr-pdfs/2026/20035134.pdf; the v9 parse
        // stored Organon at row 11 and Versant at row 13, across the reprinted page header.
        var result = ParseAsset(
            "house-ptr-hern-original.pdf",
            "Kevin Hern",
            new DateOnly(2026, 8, 10)
        );

        result
            .Transactions.Select(row => row.FilingStatus)
            .Should()
            .AllBeEquivalentTo(CongressionalTradeFilingStatus.New);
        result.Transactions.Single(row => row.Ticker == "OGN").SourceRowIndex.Should().Be(11);
        result.Transactions.Single(row => row.Ticker == "VSNT").SourceRowIndex.Should().Be(13);
    }

    [Fact]
    public void ParsePtrPdf_SpanbergerAmendment_ReadsAmendedRowWithUnchangedStoredFacts()
    {
        // https://disclosures-clerk.house.gov/public_disc/ptr-pdfs/2024/20025224.pdf amends only the
        // description of 20025205's row.
        var original = ParseAsset(
            "house-ptr-spanberger-original.pdf",
            "Abigail Spanberger",
            new DateOnly(2024, 6, 5)
        )
            .Transactions.Should()
            .ContainSingle()
            .Subject;
        var amended = ParseAsset(
            "house-ptr-spanberger-amendment.pdf",
            "Abigail Spanberger",
            new DateOnly(2024, 6, 6)
        )
            .Transactions.Should()
            .ContainSingle()
            .Subject;

        original.FilingStatus.Should().Be(CongressionalTradeFilingStatus.New);
        amended.FilingStatus.Should().Be(CongressionalTradeFilingStatus.Amended);
        amended
            .Should()
            .BeEquivalentTo(
                original,
                options =>
                    options.Excluding(row => row.FilingStatus).Excluding(row => row.FilingDate)
            );
    }

    [Theory]
    [InlineData("F      S     : Amended", CongressionalTradeFilingStatus.Amended)]
    [InlineData("FIlINg STATuS: Deleted", CongressionalTradeFilingStatus.Deleted)]
    [InlineData("Filing Status: New", CongressionalTradeFilingStatus.New)]
    [InlineData("F S: New S O: Joint Ownership LPL Account", CongressionalTradeFilingStatus.New)]
    [InlineData("Apple Inc. (AAPL) [ST] F S: Amended", CongressionalTradeFilingStatus.Amended)]
    [InlineData("Series D: New Issue Notes", CongressionalTradeFilingStatus.Unstated)]
    [InlineData("S O: Hern Family Foundation", CongressionalTradeFilingStatus.Unstated)]
    public void ReadFilingStatus_LabelVariants_ReadsTheFiledValue(
        string text,
        CongressionalTradeFilingStatus expected
    ) => HouseDisclosureClient.ReadFilingStatus(text).Should().Be(expected);

    [Fact]
    public void ParseTransactionLines_InlineDeletedStatus_EmitsTheRowWithItsIndex()
    {
        var result = HouseDisclosureClient.ParseTransactionLinesWithShape(
            [
                "Apple Inc. (AAPL) [ST] F S: Deleted P 03/03/2025 03/05/2025 $1,001 - $15,000",
                "Tesla Inc. (TSLA) [ST] P 03/04/2025 03/05/2025 $1,001 - $15,000",
                "F S: New",
            ],
            "Test Member",
            new DateOnly(2025, 3, 6)
        );

        result
            .Transactions.Select(row => (row.Ticker, row.SourceRowIndex, row.FilingStatus))
            .Should()
            .Equal(
                ("AAPL", 0, CongressionalTradeFilingStatus.Deleted),
                ("TSLA", 1, CongressionalTradeFilingStatus.New)
            );
    }
}
