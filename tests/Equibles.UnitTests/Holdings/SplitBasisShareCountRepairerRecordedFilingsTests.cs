using System.Reflection;
using System.Text.Json;
using Equibles.CorporateActions.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Replays recorded information tables through the import's own parse and per-filing repair.
/// Each filer reported a quarter that ended before a split, filed after it, and moved one figure
/// to the later basis: the count (restated by the split a second time, so Broadcom read as a
/// tenfold buy) or the value (marked at the later price, so the position published a tenth of its
/// size). The neighbouring quarters are the holders' stored positions.
/// </summary>
public class SplitBasisShareCountRepairerRecordedFilingsTests
{
    private const string Broadcom = "11135F101";

    [Fact]
    public void ForwardSplitCountFiledOnTheLaterBasis_IsRebasedAndRevalued()
    {
        var replay = Replay("0001085146-24-003837.json");
        var before = replay.OtherRows(Broadcom).Select(Snapshot).ToList();
        // The guard caught the tenfold derivation and published the filed value, but the count
        // still read 54M shares on today's basis.
        replay
            .Legs(Broadcom)
            .Should()
            .OnlyContain(leg => leg.Holding.ValueSource == ValueSource.Filed);

        var corrections = replay.Repair(neighbourQuarters: ["2024-03-31", "2024-09-30"]);

        var legs = replay.Legs(Broadcom);
        legs.Select(leg => leg.Holding.Shares).Should().Equal(85, 542_060, 830);
        legs.Select(leg => leg.ManagerEntry.Shares).Should().Equal(85, 542_060, 830);
        legs.Select(leg => leg.Holding.VotingAuthSole).Should().Equal(85, 542_060, 830);
        legs.Select(leg => leg.Holding.FiledValue).Should().Equal(136_470, 868_975_493, 1_331_818);
        legs.Select(leg => leg.Holding.Value).Should().Equal(136_470, 870_293_591, 1_332_589);
        legs.Should().OnlyContain(leg => leg.Holding.ValueSource == ValueSource.Derived);
        corrections.Should().ContainSingle();
        corrections[0].Kind.Should().Be(SplitBasisShareCountRepairer.Kind.PostSplitShares);
        corrections[0].Factor.Should().Be(10m);
        corrections[0].FiledShares.Should().Be(5_429_741);
        corrections[0].FiledValue.Should().Be(870_443_781);
        corrections[0].NeighbourShares.Should().Be(504_523);
        replay.OtherRows(Broadcom).Select(Snapshot).Should().Equal(before);
    }

    [Fact]
    public void ForwardSplitValueMarkedAtTheLaterPrice_IsScaledAndPublishesTheDerivation()
    {
        var replay = Replay("0001608046-24-000005.json");
        var before = replay.OtherRows(Broadcom).Select(Snapshot).ToList();
        var position = replay.Legs(Broadcom).Single();
        // The filed $116M stood in for a $1.16B position.
        position.Holding.Value.Should().Be(115_964_060);
        position.Holding.ValueSource.Should().Be(ValueSource.Filed);

        var corrections = replay.Repair(neighbourQuarters: ["2024-03-31", "2024-09-30"]);

        position.Holding.Shares.Should().Be(722_279);
        position.Holding.FiledValue.Should().Be(1_159_640_600);
        position.Holding.Value.Should().Be(1_159_640_602);
        position.ManagerEntry.Value.Should().Be(1_159_640_602);
        position.Holding.ValueSource.Should().Be(ValueSource.Derived);
        corrections.Should().ContainSingle();
        corrections[0].Kind.Should().Be(SplitBasisShareCountRepairer.Kind.PostSplitValue);
        corrections[0].NeighbourShares.Should().Be(713_220);
        replay.OtherRows(Broadcom).Select(Snapshot).Should().Equal(before);
    }

    [Fact]
    public void TwoForOneCountFiledOnTheLaterBasis_IsRebasedThoughTheGuardNeverFired()
    {
        const string williamsSonoma = "969904101";
        var replay = Replay("0001388028-24-000003.json");
        // Twice the filed value published as derived: under the guard's cap, so nothing caught it.
        replay
            .Legs(williamsSonoma)
            .Select(leg => leg.Holding.Value)
            .Should()
            .Equal(56_354_275, 5_647_400);

        replay.Repair(neighbourQuarters: ["2024-03-31", "2024-09-30"]);

        var legs = replay.Legs(williamsSonoma);
        legs.Select(leg => leg.Holding.Shares).Should().Equal(99_788, 10_000);
        legs.Select(leg => leg.Holding.VotingAuthSole).Should().Equal(40_650, 0);
        legs.Select(leg => leg.Holding.VotingAuthNone).Should().Equal(59_138, 10_000);
        legs.Select(leg => leg.Holding.Value).Should().Equal(28_177_137, 2_823_700);
    }

    [Fact]
    public void ReverseSplitCountFiledOnTheLaterBasis_IsRebasedUpward()
    {
        const string nextCure = "65343E108";
        var replay = Replay("0000078003-25-000142.json");
        // A twelfth of the position published as derived, with no guard for a low derivation.
        replay.Legs(nextCure).Select(leg => leg.Holding.Value).Should().Equal(12_207, 60_310);

        replay.Repair(neighbourQuarters: ["2025-03-31", "2025-09-30"]);

        var legs = replay.Legs(nextCure);
        legs.Select(leg => leg.Holding.Shares).Should().Equal(318_444, 1_573_308);
        legs.Select(leg => leg.Holding.VotingAuthShared).Should().Equal(0, 1_573_308);
        legs.Select(leg => leg.Holding.Value).Should().Equal(146_484, 723_721);
    }

    [Fact]
    public void ReverseSplitValueMarkedAtTheLaterPrice_KeepsTheCountAndScalesTheFiledValue()
    {
        const string amcor = "G0250X107";
        var replay = Replay("0001062993-26-000784.json");

        var corrections = replay.Repair(neighbourQuarters: ["2025-09-30"]);

        var position = replay.Legs(amcor).Single();
        position.Holding.Shares.Should().Be(13_498_709);
        position.Holding.FiledValue.Should().Be(112_579_233);
        position.Holding.Value.Should().Be(112_579_233);
        corrections.Single().Kind.Should().Be(SplitBasisShareCountRepairer.Kind.PostSplitValue);
    }

    // The two errors look identical within the filing; without a neighbour the count is taken as
    // late, the more common error.
    [Fact]
    public void NoNeighbouringQuarter_TakesTheCountAsLate()
    {
        var replay = Replay("0001608046-24-000005.json");

        var corrections = replay.Repair(neighbourQuarters: []);

        corrections.Single().Kind.Should().Be(SplitBasisShareCountRepairer.Kind.PostSplitShares);
        corrections[0].NeighbourShares.Should().BeNull();
        replay.Legs(Broadcom).Single().Holding.Shares.Should().Be(72_228);
    }

    // The next quarter is already on the later basis; restated back, it still names the count.
    [Fact]
    public void OnlyTheNextQuarter_IsRestatedOntoTheReportDateBeforeJudging()
    {
        var replay = Replay("0001085146-24-003837.json");

        var corrections = replay.Repair(neighbourQuarters: ["2024-09-30"]);

        corrections.Single().Kind.Should().Be(SplitBasisShareCountRepairer.Kind.PostSplitShares);
        corrections[0].NeighbourShares.Should().Be(484_464);
    }

    [Fact]
    public void SplitTakingEffectAfterTheFiling_LeavesTheFilingAsFiled()
    {
        var replay = Replay("0001608046-24-000005.json", effectiveDate: new DateOnly(2024, 8, 20));
        var before = replay.Rows.Select(Snapshot).ToList();

        var corrections = replay.Repair(neighbourQuarters: ["2024-03-31"]);

        corrections.Should().BeEmpty();
        replay.Rows.Select(Snapshot).Should().Equal(before);
    }

    // A quarter whose other holders do not confirm the price basis may be priced on the wrong
    // side of the split itself, so nothing in it is rebased.
    [Fact]
    public void QuarterNotConfirmingItsPriceBasis_LeavesTheFilingAsFiled()
    {
        var replay = Replay("0001085146-24-003837.json");
        var before = replay.Rows.Select(Snapshot).ToList();

        var corrections = replay.Repair(neighbourQuarters: ["2024-03-31"], basisConfirmed: false);

        corrections.Should().BeEmpty();
        replay.Rows.Select(Snapshot).Should().Equal(before);
    }

    private static (long, long, long?, long, long, bool, ValueSource) Snapshot(
        BufferedHoldingRow row
    ) =>
        (
            row.Holding.Shares,
            row.Holding.Value,
            row.Holding.FiledValue,
            row.ManagerEntry.Shares,
            row.Holding.VotingAuthSole + row.Holding.VotingAuthShared + row.Holding.VotingAuthNone,
            row.Holding.ValuePending,
            row.Holding.ValueSource
        );

    private sealed record RecordedFiling(
        string Accession,
        List<BufferedHoldingRow> Rows,
        ImportContext Context,
        Dictionary<string, List<(DateOnly ReportDate, long Shares)>> StoredQuarters
    )
    {
        public List<BufferedHoldingRow> Legs(string cusip) =>
            Rows.Where(row => row.Holding.Cusip == cusip).ToList();

        public List<BufferedHoldingRow> OtherRows(string cusip) =>
            Rows.Where(row => row.Holding.Cusip != cusip).ToList();

        // The import's own neighbour restatement over the holder's stored quarters, limited to the
        // named report dates. The quarter's other holders confirm its price basis unless told not.
        public List<SplitBasisCorrection> Repair(
            string[] neighbourQuarters,
            bool basisConfirmed = true
        )
        {
            var dates = neighbourQuarters.Select(DateOnly.Parse).ToHashSet();
            var neighbours = new Dictionary<string, long>();
            foreach (var position in Rows.Select(row => row.Holding))
            {
                if (!StoredQuarters.TryGetValue(position.Cusip, out var quarters))
                    continue;
                if (
                    HoldingsImportService.SplitBasisNeighbour(
                        position,
                        quarters.Where(quarter => dates.Contains(quarter.ReportDate)),
                        Context
                    ) is
                    { } shares
                )
                    neighbours[HoldingsImportService.BuildHoldingKey(position)] = shares;
            }
            var confirmed = basisConfirmed
                ? Rows.Select(row => HoldingsImportService.BuildHoldingKey(row.Holding)).ToHashSet()
                : [];
            return HoldingsImportService.RepairFiling(
                Accession,
                Rows,
                Context,
                NullLogger.Instance,
                new SplitBasisInputs(neighbours, confirmed)
            );
        }
    }

    private static readonly MethodInfo ParseHoldingRow = typeof(HoldingsImportService).GetMethod(
        "ParseHoldingRow",
        BindingFlags.NonPublic | BindingFlags.Static
    );

    // Each CUSIP stands for its own issuer. The split's issuer is priced at the stored quarter-end
    // close on today's basis and carries the split captured on its primary listing.
    private static RecordedFiling Replay(string fileName, DateOnly? effectiveDate = null)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "TestAssets",
                    "Holdings",
                    "SplitBasis",
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
            var effective = effectiveDate ?? DateOnly.Parse(split[2].GetString());
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
        var storedQuarters = new Dictionary<string, List<(DateOnly, long)>>();
        foreach (var neighbour in root.GetProperty("neighbours").EnumerateArray())
        {
            var cusip = neighbour[0].GetString();
            if (!storedQuarters.TryGetValue(cusip, out var quarters))
                storedQuarters[cusip] = quarters = [];
            quarters.Add((DateOnly.Parse(neighbour[1].GetString()), neighbour[2].GetInt64()));
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
                ["VOTING_AUTH_SOLE"] = source[5].GetInt64().ToString(),
                ["VOTING_AUTH_SHARED"] = source[6].GetInt64().ToString(),
                ["VOTING_AUTH_NONE"] = source[7].GetInt64().ToString(),
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

        return new RecordedFiling(accession, rows, context, storedQuarters);
    }
}
