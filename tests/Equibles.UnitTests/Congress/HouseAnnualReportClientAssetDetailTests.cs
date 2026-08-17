using Equibles.Congress.Data.Models;
using Equibles.Congress.HostedService.Services;
using static Equibles.Congress.HostedService.Services.HouseAnnualReportClient;

namespace Equibles.UnitTests.Congress;

/// <summary>
/// Schedule A carries three details beyond the asset's value: the filer's
/// bracketed asset-class code, the income type(s) the asset produced, and the
/// income bracket. The income columns sit to the right of the value column and
/// wrap the same way it does, so these tests pin both the column windows (the
/// header holds TWO "Income" tokens) and the wrap handling.
/// </summary>
public class HouseAnnualReportClientAssetDetailTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "Congress", name);

    // Column left edges taken from the real Clerk layout:
    // "Asset@25 Owner@241 Value@280 of@311 Asset@323 Income@363 Type(s)@402
    //  Income@445 Tx.@534 >@551".
    private static List<ScheduleToken> Header() =>
        [
            new("Asset", 25),
            new("Owner", 241),
            new("Value", 280),
            new("of", 311),
            new("Asset", 323),
            new("Income", 363),
            new("Type(s)", 402),
            new("Income", 445),
            new("Tx.", 534),
            new(">", 551),
        ];

    // Rows arrive as one collection expression so their element type is
    // unambiguously List<ScheduleToken> — a params array would target-type the
    // inner "new(...)" elements at the list itself.
    private static List<List<ScheduleToken>> Schedule(List<List<ScheduleToken>> rows) =>
        [
            [new("S", 25), new("A:", 40)],
            Header(),
            .. rows,
        ];

    [Fact]
    public void ParseScheduleLines_AssetRow_CapturesTypeCodeAndIncome()
    {
        var lines = Schedule([
            [
                new("Acme", 25),
                new("Corp", 60),
                new("[ST]", 100),
                new("SP", 241),
                new("$1,001", 280),
                new("-", 320),
                new("$15,000", 330),
                new("Dividends", 363),
                new("$201", 445),
                new("-", 470),
                new("$1,000", 480),
            ],
        ]);

        var item = HouseAnnualReportClient.ParseScheduleLines(lines).Single();

        item.Description.Should().Be("Acme Corp");
        item.AssetType.Should().Be("ST");
        item.IncomeType.Should().Be("Dividends");
        item.IncomeMinimum.Should().Be(201);
        item.IncomeMaximum.Should().Be(1_000);
    }

    [Fact]
    public void ParseScheduleLines_IncomeBracketWrapsToNextLine_ReassemblesIt()
    {
        // Both the value and the income bracket wrap their upper bound onto the
        // following visual line, each landing back under its own column.
        var lines = Schedule([
            [
                new("Acme", 25),
                new("Corp", 60),
                new("[ST]", 100),
                new("SP", 241),
                new("$1,000,001", 280),
                new("-", 334),
                new("Capital", 363),
                new("Gains,", 393),
                new("$15,001", 445),
                new("-", 490),
            ],
            [new("$5,000,000", 280), new("Dividends", 363), new("$50,000", 445)],
        ]);

        var item = HouseAnnualReportClient.ParseScheduleLines(lines).Single();

        item.RangeMinimum.Should().Be(1_000_001);
        item.RangeMaximum.Should().Be(5_000_000);
        item.IncomeType.Should().Be("Capital Gains, Dividends");
        item.IncomeMinimum.Should().Be(15_001);
        item.IncomeMaximum.Should().Be(50_000);
    }

    [Fact]
    public void ParseScheduleLines_IncomeBracketNeverClosed_LeavesIncomeUnset()
    {
        // The wrapped upper bound never arrived. Re-deriving one would invent a
        // bracket the filer did not check, so income stays unset while the row
        // itself — whose own value bracket is complete — survives.
        var lines = Schedule([
            [
                new("Acme", 25),
                new("Corp", 60),
                new("[ST]", 100),
                new("SP", 241),
                new("$1,001", 280),
                new("-", 320),
                new("$15,000", 330),
                new("Rent", 363),
                new("$15,001", 445),
                new("-", 490),
            ],
        ]);

        var item = HouseAnnualReportClient.ParseScheduleLines(lines).Single();

        item.RangeMinimum.Should().Be(1_001);
        item.IncomeType.Should().Be("Rent");
        item.IncomeMinimum.Should().BeNull();
        item.IncomeMaximum.Should().BeNull();
    }

    [Fact]
    public void ParseScheduleLines_IncomeReportedAsNone_LeavesIncomeUnset()
    {
        var lines = Schedule([
            [
                new("Acme", 25),
                new("Corp", 60),
                new("[BA]", 100),
                new("SP", 241),
                new("$1,001", 280),
                new("-", 320),
                new("$15,000", 330),
                new("None", 363),
                new("None", 445),
            ],
        ]);

        var item = HouseAnnualReportClient.ParseScheduleLines(lines).Single();

        item.AssetType.Should().Be("BA");
        item.IncomeType.Should().BeNull();
        item.IncomeMinimum.Should().BeNull();
        item.IncomeMaximum.Should().BeNull();
    }

    [Fact]
    public void ParseScheduleLines_IncomeColumnAbsentFromHeader_LeavesIncomeUnset()
    {
        // A layout with no separate income-amount column must not let the
        // income-type text bleed into the amount window.
        List<List<ScheduleToken>> lines =
        [
            [new("S", 25), new("A:", 40)],
            [new("Asset", 25), new("Owner", 241), new("Value", 280), new("Income", 363)],
            [
                new("Acme", 25),
                new("Corp", 60),
                new("[ST]", 100),
                new("SP", 241),
                new("$1,001", 280),
                new("-", 320),
                new("$15,000", 330),
                new("Dividends", 363),
            ],
        ];

        var item = HouseAnnualReportClient.ParseScheduleLines(lines).Single();

        item.IncomeType.Should().Be("Dividends");
        item.IncomeMinimum.Should().BeNull();
        item.IncomeMaximum.Should().BeNull();
    }

    [Fact]
    public void ParseScheduleLines_TypeCodeOnWrappedNameLine_IsStillCaptured()
    {
        // A long asset name wraps and takes its code with it onto the last
        // visual line, so the code cannot be read once off the opening line.
        var lines = Schedule([
            [
                new("City", 25),
                new("National", 50),
                new("Securities", 95),
                new("-", 150),
                new("Brokerage", 160),
                new("SP", 241),
                new("$1,001", 280),
                new("-", 320),
                new("$15,000", 330),
            ],
            [new("Money", 25), new("Market", 60), new("Account", 100), new("[BA]", 150)],
        ]);

        var item = HouseAnnualReportClient.ParseScheduleLines(lines).Single();

        item.Description.Should().Be("City National Securities - Brokerage Money Market Account");
        item.AssetType.Should().Be("BA");
    }

    [Fact]
    public void ParseScheduleLines_LiabilityRow_HasNoAssetDetail()
    {
        // ParseScheduleLines only returns items once it has seen Schedule A, so
        // the report opens with an empty one before the liabilities.
        List<List<ScheduleToken>> lines =
        [
            [new("S", 25), new("A:", 40)],
            Header(),
            [new("S", 25), new("D:", 40)],
            [
                new("Owner", 25),
                new("Creditor", 80),
                new("Date", 200),
                new("Incurred", 240),
                new("Type", 320),
                new("Amount", 420),
            ],
            [
                new("SP", 25),
                new("Bank", 80),
                new("2019", 200),
                new("Mortgage", 320),
                new("$1,001", 420),
                new("-", 470),
                new("$15,000", 480),
            ],
        ];

        var item = HouseAnnualReportClient.ParseScheduleLines(lines).Single();

        item.Kind.Should().Be(CongressionalDisclosureLineKind.Liability);
        item.AssetType.Should().BeNull();
        item.IncomeType.Should().BeNull();
        item.IncomeMinimum.Should().BeNull();
    }

    [Fact]
    public void ParseAnnualReportPdf_RealFiling_CarriesAssetDetail()
    {
        var bytes = File.ReadAllBytes(FixturePath("house-annual-pelosi-2024.pdf"));

        var lines = HouseAnnualReportClient.ParseAnnualReportPdf(bytes).Lines;

        var vineyard = lines.Single(l => l.Description == "11 Zinfandel Lane - Home & Vineyard");
        vineyard.AssetType.Should().Be("RP");
        vineyard.IncomeType.Should().Be("Grape Sales");
        vineyard.IncomeMinimum.Should().Be(100_001);
        vineyard.IncomeMaximum.Should().Be(1_000_000);

        // "Over $5,000,000" is the form's open-top bracket, which this module
        // represents as (value, value) — the same convention the value column
        // already uses.
        var apple = lines.Single(l => l.Description == "Apple Inc. (AAPL)");
        apple.AssetType.Should().Be("ST");
        apple.IncomeType.Should().Be("Capital Gains, Dividends");
        apple.IncomeMinimum.Should().Be(5_000_000);
        apple.IncomeMaximum.Should().Be(5_000_000);

        // The name wrapped and carried "[BA]" onto its second line.
        lines
            .Single(l =>
                l.Description == "City National Securities - Brokerage Money Market Account"
            )
            .AssetType.Should()
            .Be("BA");

        lines
            .Where(l => l.Kind == CongressionalDisclosureLineKind.Liability)
            .Should()
            .OnlyContain(l => l.AssetType == null && l.IncomeType == null);
    }
}
