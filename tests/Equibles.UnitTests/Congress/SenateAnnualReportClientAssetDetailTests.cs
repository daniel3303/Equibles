using Equibles.Congress.HostedService.Services;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Part 3 of an eFD annual report carries an Asset Type, Income Type and Income
/// column beside the value. The type cell splits its classification across the
/// main text and a muted child ("Corporate Securities" / "Non-Public Stock") —
/// both halves are disclosed detail, unlike the muted location metadata on the
/// asset-name column, which stays out of the description.
/// </summary>
public class SenateAnnualReportClientAssetDetailTests
{
    private static string Report(string assetRows) =>
        $"""
            <html><body>
            <h3>Part 3. Assets</h3>
            <table>
              <thead><tr>
                <th>#</th><th>Asset</th><th>Asset Type</th><th>Owner</th>
                <th>Value</th><th>Income Type</th><th>Income</th>
              </tr></thead>
              <tbody>{assetRows}</tbody>
            </table>
            </body></html>
            """;

    [Fact]
    public void ParseAnnualReportHtml_AssetRow_CapturesTypeAndIncome()
    {
        var html = Report(
            """
            <tr><td>1</td><td>ABALX - American Funds</td>
                <td>Mutual Funds<div class="muted">Mutual Fund</div></td>
                <td>Self</td><td>$15,001 - $50,000</td>
                <td>Dividends, Capital Gains</td><td>$1,001 - $2,500</td></tr>
            """
        );

        var item = SenateAnnualReportClient.ParseAnnualReportHtml(html).Single();

        item.Description.Should().Be("ABALX - American Funds");
        item.AssetType.Should().Be("Mutual Funds - Mutual Fund");
        item.IncomeType.Should().Be("Dividends, Capital Gains");
        item.IncomeMinimum.Should().Be(1_001);
        item.IncomeMaximum.Should().Be(2_500);
    }

    [Fact]
    public void ParseAnnualReportHtml_TypeCellWithoutSubtype_KeepsTheCategoryAlone()
    {
        var html = Report(
            """
            <tr><td>1</td><td>Payflex Systems USA INC</td><td>Bank Deposit</td>
                <td>Self</td><td>$15,001 - $50,000</td>
                <td>None</td><td>None (or less than $201)</td></tr>
            """
        );

        var item = SenateAnnualReportClient.ParseAnnualReportHtml(html).Single();

        item.AssetType.Should().Be("Bank Deposit");
        // "None" is the filer answering the question, and the sub-$201 floor is
        // not a disclosed bracket — neither is stored as a value.
        item.IncomeType.Should().BeNull();
        item.IncomeMinimum.Should().BeNull();
        item.IncomeMaximum.Should().BeNull();
    }

    [Fact]
    public void ParseAnnualReportHtml_LayoutWithoutDetailColumns_LeavesDetailUnset()
    {
        // An older table missing the detail columns must leave them unset rather
        // than reading whichever column happens to sit at that index.
        var html = """
            <html><body>
            <h3>Part 3. Assets</h3>
            <table>
              <thead><tr><th>#</th><th>Asset</th><th>Owner</th><th>Value</th></tr></thead>
              <tbody><tr><td>1</td><td>Some Fund</td><td>Self</td>
                <td>$15,001 - $50,000</td></tr></tbody>
            </table>
            </body></html>
            """;

        var item = SenateAnnualReportClient.ParseAnnualReportHtml(html).Single();

        item.RangeMinimum.Should().Be(15_001);
        item.AssetType.Should().BeNull();
        item.IncomeType.Should().BeNull();
        item.IncomeMinimum.Should().BeNull();
    }

    [Fact]
    public void ParseAnnualReportHtml_RealFiling_CarriesAssetDetail()
    {
        var html = File.ReadAllText(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Congress",
                "senate-annual-blackburn-2024.html"
            )
        );

        var items = SenateAnnualReportClient.ParseAnnualReportHtml(html);

        var deposit = items.Single(i => i.Description == "Payflex Systems USA INC");
        deposit.AssetType.Should().Be("Bank Deposit");
        deposit.IncomeMinimum.Should().BeNull();

        var nonPublic = items.Single(i => i.Description == "Strategic Sales Tactics Inc.");
        nonPublic.AssetType.Should().Be("Corporate Securities - Non-Public Stock");

        var balanced = items.Single(i =>
            i.Description == "ABALX - American Funds American Balanced Fund Class A"
        );
        balanced.AssetType.Should().Be("Mutual Funds - Mutual Fund");
        balanced.IncomeType.Should().Be("Dividends, Capital Gains");
        balanced.IncomeMinimum.Should().Be(1_001);
        balanced.IncomeMaximum.Should().Be(2_500);
    }
}
