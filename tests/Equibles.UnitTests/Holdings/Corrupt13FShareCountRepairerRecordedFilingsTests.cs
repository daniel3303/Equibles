using System.Reflection;
using System.Text.Json;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Replays a recorded information table whose share-count column repeats the value column through
/// the import's own parse and per-filing repair. Three of its stocks split after the quarter, so
/// their stored closes are on a later basis than the filing's counts.
/// </summary>
public class Corrupt13FShareCountRepairerRecordedFilingsTests
{
    [Fact]
    public void StockThatSplitAfterTheQuarter_RecoversTheCountOnTheFilingsOwnBasis()
    {
        var replay = Replay("0002011335-26-000002.json");

        HoldingsImportService.RepairFiling(
            replay.Accession,
            replay.Rows,
            replay.Context,
            NullLogger.Instance
        );

        // Booking split 25:1 in April 2026: $2,832,970 at the $5,355.33 the shares traded at on
        // the report date is 529 shares, the count the filer's voting column states.
        var booking = replay.Row("09857L108");
        booking.Holding.Shares.Should().Be(529);
        booking.ManagerEntry.Shares.Should().Be(529);
        booking.Holding.Value.Should().Be(2_832_969);
        // Amphenol split 2:1 in September 2026, and Honeywell's count was restated 1907:2000.
        replay.Row("032095101").Holding.Shares.Should().Be(568_076);
        replay.Row("438516106").Holding.Shares.Should().Be(272_741);
    }

    [Fact]
    public void EveryRow_RecoversTheCountTheVotingColumnStatesAndTheValueAsFiled()
    {
        var replay = Replay("0002011335-26-000002.json");

        HoldingsImportService.RepairFiling(
            replay.Accession,
            replay.Rows,
            replay.Context,
            NullLogger.Instance
        );

        replay.Rows.Should().HaveCount(43);
        replay.Rows.Should().OnlyContain(row => row.Holding.Shares == row.Holding.VotingAuthSole);
        replay
            .Rows.Should()
            .OnlyContain(row =>
                !row.Holding.ValuePending
                && !row.Holding.ValueUnavailable
                && row.Holding.ValueSource == ValueSource.Derived
            );
        // The filing states $1,173,366,950; each row lands within half a share of its own figure.
        replay.Rows.Sum(row => row.Holding.Value).Should().BeInRange(1_173_350_000, 1_173_380_000);
    }

    private sealed record RecordedFiling(
        string Accession,
        List<BufferedHoldingRow> Rows,
        ImportContext Context
    )
    {
        public BufferedHoldingRow Row(string cusip) =>
            Rows.Single(row => row.Holding.Cusip == cusip);
    }

    private static readonly MethodInfo ParseHoldingRow = typeof(HoldingsImportService).GetMethod(
        "ParseHoldingRow",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    // Each CUSIP stands for its own issuer, priced at its stored quarter-end close on today's
    // basis; an issuer that split afterwards carries the split on its primary listing.
    private static RecordedFiling Replay(string fileName)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "TestAssets",
                    "Holdings",
                    "DuplicatedColumns",
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
        foreach (var split in root.GetProperty("splits").EnumerateArray())
        {
            var issuer = Issuer(split[0].GetString());
            var ticker = split[1].GetString();
            var effective = DateOnly.Parse(split[2].GetString());
            context.PrimaryTickers[issuer] = ticker;
            context.StockSplits[issuer] =
            [
                new StockSplit
                {
                    EquityIssuerId = issuer,
                    PriceSeriesTicker = ticker,
                    EffectiveDate = effective,
                    Numerator = split[3].GetDecimal(),
                    Denominator = split[4].GetDecimal(),
                    PriceAdjustmentAppliedTime = effective
                        .AddDays(1)
                        .ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    ShareCountEvidence = StockSplitShareCountEvidence.Confirmed,
                },
            ];
        }

        var holder = Guid.NewGuid();
        var rows = new List<BufferedHoldingRow>();
        foreach (var source in root.GetProperty("rows").EnumerateArray())
        {
            var cusip = source[0].GetString();
            var value = source[1].GetInt64().ToString();
            var fields = new Dictionary<string, string>
            {
                ["SSHPRNAMTTYPE"] = "SH",
                ["PUTCALL"] = "",
                ["SSHPRNAMT"] = value,
                ["VALUE"] = value,
                ["VOTING_AUTH_SOLE"] = source[2].GetInt64().ToString(),
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

        return new RecordedFiling(accession, rows, context);
    }
}
