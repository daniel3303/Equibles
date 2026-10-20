using System.Reflection;
using System.Text.Json;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Replays recorded information tables through the import's own parse and per-filing repair.
/// Filings that wrote each position's dollar value into the share column and its share count into
/// the value column stored books inflated by the share price; their next quarter then read as
/// trillions of dollars sold.
/// </summary>
public class Swapped13FColumnRepairerRecordedFilingsTests
{
    [Fact]
    public void ThousandsEraFilingWithSwappedColumns_IsSwappedBackAndRevalued()
    {
        var replay = Replay("0001737088-22-000002.json");

        replay.BookBefore.Should().Be(3_581_032_591L);

        HoldingsImportService.RepairFiling(
            replay.Accession,
            replay.Rows,
            replay.Context,
            NullLogger.Instance
        );

        // The real book is the share column's dollars, $223,389,902; the 45 unpriced rows wait
        // for the repricing lane with their corrected counts.
        replay.Book().Should().Be(219_739_168L);
        replay.Rows.Count(row => row.Holding.ValuePending).Should().Be(45);
        replay.Rows.Count(row => row.Holding.ValueSource == ValueSource.Filed).Should().Be(2);
        // 22 class A shares at $450,662, filed as 9,914,564 shares worth $22,000.
        var row = replay.Row("084670108");
        row.Holding.Shares.Should().Be(22);
        row.ManagerEntry.Shares.Should().Be(22);
        row.Holding.FiledValue.Should().Be(9_914_564);
        row.Holding.Value.Should().Be(9_914_564);
        row.ManagerEntry.Value.Should().Be(9_914_564);
        row.Holding.ValueSource.Should().Be(ValueSource.Derived);
        row.Holding.ValuePending.Should().BeFalse();
    }

    [Fact]
    public void WholeDollarFilingWithSwappedColumns_IsSwappedBackAndRevalued()
    {
        var replay = Replay("0001977602-23-000001.json");

        // The sanity guard caught most rows and published the value column: the share counts.
        replay.BookBefore.Should().Be(340_545_766L);

        HoldingsImportService.RepairFiling(
            replay.Accession,
            replay.Rows,
            replay.Context,
            NullLogger.Instance
        );

        replay.Book().Should().Be(2_319_697_732L);
        replay.Rows.Should().OnlyContain(row => row.Holding.ValueSource == ValueSource.Derived);
        var row = replay.Row("594918104");
        row.Holding.Shares.Should().Be(842_922);
        row.Holding.FiledValue.Should().Be(243_014_413);
        row.Holding.Value.Should().Be(243_014_412);
        row.Holding.ValueSource.Should().Be(ValueSource.Derived);
    }

    [Fact]
    public void SameFilerCorrectlyFiledNextQuarter_IsLeftAsFiled()
    {
        var replay = Replay("0001737088-22-000005.json");
        var before = replay.Rows.Select(Snapshot).ToList();

        HoldingsImportService.RepairFiling(
            replay.Accession,
            replay.Rows,
            replay.Context,
            NullLogger.Instance
        );

        replay.Rows.Select(Snapshot).Should().Equal(before);
        replay.Book().Should().Be(replay.BookBefore);
    }

    private static (long, long, long?, long, bool) Snapshot(BufferedHoldingRow row) =>
        (
            row.Holding.Shares,
            row.Holding.Value,
            row.Holding.FiledValue,
            row.ManagerEntry.Shares,
            row.Holding.ValuePending
        );

    private sealed record RecordedFiling(
        string Accession,
        List<BufferedHoldingRow> Rows,
        ImportContext Context,
        long BookBefore
    )
    {
        public long Book() => Rows.Sum(row => row.Holding.Value);

        public BufferedHoldingRow Row(string cusip) =>
            Rows.Single(row => row.Holding.Cusip == cusip);
    }

    private static readonly MethodInfo ParseHoldingRow = typeof(HoldingsImportService).GetMethod(
        "ParseHoldingRow",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    // Each CUSIP stands for its own issuer, priced at the recorded quarter-end close on the
    // filing's share basis.
    private static RecordedFiling Replay(string fileName)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "TestAssets",
                    "Holdings",
                    "SwappedColumns",
                    fileName
                )
            )
        );
        var root = document.RootElement;
        var accession = root.GetProperty("accession").GetString();
        var filingDate = DateOnly.Parse(root.GetProperty("filingDate").GetString());
        var reportDate = DateOnly.Parse(root.GetProperty("reportDate").GetString());
        var issuers = new Dictionary<string, Guid>();
        Guid Issuer(string cusip) =>
            issuers.TryGetValue(cusip, out var id) ? id : issuers[cusip] = Guid.NewGuid();

        var context = new ImportContext
        {
            CoverPages = new Dictionary<string, CoverPageRow>(),
            OtherManagers = new Dictionary<string, Dictionary<int, OtherManagerIdentity>>(),
        };
        foreach (var price in root.GetProperty("prices").EnumerateArray())
        {
            var cusip = price[0].GetString();
            context.StockPrices[(Issuer(cusip), null, reportDate)] = price[1].GetDecimal();
        }

        var holder = Guid.NewGuid();
        var rows = new List<BufferedHoldingRow>();
        foreach (var source in root.GetProperty("rows").EnumerateArray())
        {
            var cusip = source[0].GetString();
            var fields = new Dictionary<string, string>
            {
                ["SSHPRNAMTTYPE"] = source[1].GetString(),
                ["PUTCALL"] = source[2].GetString(),
                ["SSHPRNAMT"] = source[3].GetInt64().ToString(),
                ["VALUE"] = source[4].GetInt64().ToString(),
                ["VOTING_AUTH_SOLE"] = "0",
                ["VOTING_AUTH_SHARED"] = "0",
                ["VOTING_AUTH_NONE"] = "0",
                ["OTHERMANAGER"] = "",
                ["INVESTMENTDISCRETION"] = "SOLE",
                ["TITLEOFCLASS"] = "COM",
            };
            var parsed = ParseHoldingRow.Invoke(
                null,
                [
                    fields,
                    accession,
                    cusip,
                    new CusipTarget(Issuer(cusip), null),
                    holder,
                    filingDate,
                    reportDate,
                    context,
                ]
            );
            var type = parsed.GetType();
            rows.Add(
                new BufferedHoldingRow
                {
                    Holding = (InstitutionalHolding)type.GetField("Item1").GetValue(parsed),
                    ManagerEntry = (HoldingManagerEntry)type.GetField("Item2").GetValue(parsed),
                    ReportedValue = (long)type.GetField("Item4").GetValue(parsed),
                }
            );
        }

        return new RecordedFiling(accession, rows, context, rows.Sum(row => row.Holding.Value));
    }
}
