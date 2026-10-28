using Equibles.CommonStocks.Data.Models;
using Equibles.Core.Contracts;
using Equibles.CorporateActions.Data.Models;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Against Postgres: stored Broadcom Q2 2024 positions filed after its 10:1 split, one with the
/// count on the later basis and one with the value marked at the later price, are rebased and
/// reset for repricing, the recalculator then values both on the report-date basis, evidence
/// keeps the filed figures once, and a consistent position is left alone.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class SplitBasisHoldingRepairServiceTests : IAsyncLifetime
{
    private static readonly DateOnly PriorQuarter = new(2024, 3, 31);
    private static readonly DateOnly ReportDate = new(2024, 6, 30);
    private static readonly DateOnly SplitDate = new(2024, 7, 15);

    // The stored close on today's basis: $1,605.53 a share on the report date's.
    private const decimal Close = 160.553m;

    private readonly ParadeDbFixture _fixture;
    private readonly IStockPriceProvider _priceProvider = Substitute.For<IStockPriceProvider>();
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public SplitBasisHoldingRepairServiceTests(ParadeDbFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private EquiblesFinancialDbContext CreateSharedContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    private IServiceScopeFactory CreateScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(CreateSharedContext());
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    private SplitBasisHoldingRepairService CreateService() =>
        new(
            CreateScopeFactory(),
            _priceProvider,
            Substitute.For<ILogger<SplitBasisHoldingRepairService>>()
        );

    private HoldingsValueRecalculator CreateRecalculator() =>
        new(
            CreateScopeFactory(),
            _priceProvider,
            Substitute.For<ILogger<HoldingsValueRecalculator>>()
        );

    private static InstitutionalHolding Position(
        EquityIssuer issuer,
        InstitutionalHolder holder,
        DateOnly reportDate,
        DateOnly filingDate,
        string accession,
        long filedValue,
        ValueSource valueSource,
        params long[] legs
    )
    {
        var shares = legs.Sum();
        return new InstitutionalHolding
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = issuer.Id,
            InstitutionalHolderId = holder.Id,
            ReportDate = reportDate,
            FilingDate = filingDate,
            Shares = shares,
            VotingAuthSole = shares,
            FiledValue = filedValue,
            Value = filedValue,
            ValueSource = valueSource,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            Cusip = "11135F101",
            AccessionNumber = accession,
            CreationTime = DateTime.UtcNow.AddDays(-30),
            ManagerEntries = legs.Select(leg => new HoldingManagerEntry
                {
                    ManagerNumber = null,
                    Shares = leg,
                    Value = (long)(filedValue * ((decimal)leg / shares)),
                    InvestmentDiscretion = InvestmentDiscretion.Sole,
                })
                .ToList(),
        };
    }

    private async Task<(
        InstitutionalHolding LateCount,
        InstitutionalHolding LateValue,
        InstitutionalHolding Consistent
    )> SeedBroadcomQuarter(decimal close = Close, Guid? splitId = null)
    {
        var seed = CreateSharedContext();
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AVGO",
            Name: "Broadcom Inc."
        );
        var scotia = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "9631",
            Name = "Bank of Nova Scotia",
        };
        var pension = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "1608046",
            Name = "National Pension Service",
        };
        var consistent = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "1000001",
            Name = "Consistent filer",
        };
        var lateCount = Position(
            issuer,
            scotia,
            ReportDate,
            new DateOnly(2024, 8, 14),
            "0001085146-24-003837",
            870_443_781L,
            ValueSource.Filed,
            5_420_597L,
            9_144L
        );
        var lateValue = Position(
            issuer,
            pension,
            ReportDate,
            new DateOnly(2024, 8, 13),
            "0001608046-24-000005",
            115_964_060L,
            ValueSource.Filed,
            722_279L
        );
        var onBasis = Position(
            issuer,
            consistent,
            ReportDate,
            new DateOnly(2024, 8, 14),
            "0001000001-24-000002",
            160_553_000L,
            ValueSource.Derived,
            100_000L
        );
        seed.Set<EquityIssuer>().Add(issuer);
        // Holders that filed before the split took effect: the quarter's control, never judged.
        var early = Enumerable
            .Range(0, 2)
            .Select(i => new InstitutionalHolder
            {
                Id = Guid.NewGuid(),
                Cik = $"100000{i + 2}",
                Name = $"Early filer {i}",
            })
            .ToList();
        seed.Set<InstitutionalHolder>().AddRange([scotia, pension, consistent, .. early]);
        seed.Set<InstitutionalHolding>()
            .AddRange(
                early.Select(
                    (holder, i) =>
                        Position(
                            issuer,
                            holder,
                            ReportDate,
                            new DateOnly(2024, 7, 10),
                            $"000100000{i + 2}-24-000002",
                            80_276_500L,
                            ValueSource.Derived,
                            50_000L
                        )
                )
            );
        seed.Set<InstitutionalHolding>()
            .AddRange(
                lateCount,
                lateValue,
                onBasis,
                Position(
                    issuer,
                    scotia,
                    PriorQuarter,
                    new DateOnly(2024, 5, 15),
                    "0001085146-24-002485",
                    668_700_389L,
                    ValueSource.Derived,
                    504_523L
                ),
                Position(
                    issuer,
                    pension,
                    PriorQuarter,
                    new DateOnly(2024, 5, 14),
                    "0001608046-24-000004",
                    945_308_920L,
                    ValueSource.Derived,
                    713_220L
                )
            );
        await seed.SaveChangesAsync();
        seed.Add(
            new StockSplit
            {
                Id = splitId ?? Guid.NewGuid(),
                EquityIssuerId = issuer.Id,
                EquityListingId = issuer.Presentation.EquityListingId,
                PriceSeriesTicker = "AVGO",
                EffectiveDate = SplitDate,
                Numerator = 10m,
                Denominator = 1m,
                Source = StockSplitSource.External,
                PriceAdjustmentAppliedTime = new DateTime(2024, 7, 16, 0, 0, 0, DateTimeKind.Utc),
                ShareCountEvidence = StockSplitShareCountEvidence.Confirmed,
            }
        );
        await seed.SaveChangesAsync();

        _priceProvider
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, string, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new Dictionary<(Guid, string, DateOnly), decimal>
                {
                    [(issuer.Id, null, ReportDate)] = close,
                    [(issuer.Id, null, PriorQuarter)] = 132.541m,
                }
            );
        return (lateCount, lateValue, onBasis);
    }

    private async Task<Dictionary<Guid, InstitutionalHolding>> Reload() =>
        await CreateSharedContext()
            .Set<InstitutionalHolding>()
            .AsNoTracking()
            .ToDictionaryAsync(h => h.Id);

    [Fact]
    public async Task Repair_PositionsOnTheLaterBasis_AreRebasedThenValuedByTheRecalculator()
    {
        var (lateCount, lateValue, consistent) = await SeedBroadcomQuarter();

        var corrected = await CreateService().Repair(CancellationToken.None);

        corrected.Should().Be(2);
        var rows = await Reload();
        var count = rows[lateCount.Id];
        count.Shares.Should().Be(542_974);
        count.VotingAuthSole.Should().Be(542_974);
        count.ManagerEntries.Select(e => e.Shares).Should().BeEquivalentTo([542_060L, 914L]);
        count.FiledValue.Should().Be(870_443_781);
        count.ValuePending.Should().BeTrue();
        count.Value.Should().Be(0);
        var value = rows[lateValue.Id];
        value.Shares.Should().Be(722_279);
        value.FiledValue.Should().Be(1_159_640_600);
        value.ValuePending.Should().BeTrue();
        rows[consistent.Id].Value.Should().Be(160_553_000);
        rows[consistent.Id].ValuePending.Should().BeFalse();

        await CreateRecalculator().Recalculate(CancellationToken.None);

        rows = await Reload();
        rows[lateCount.Id].Value.Should().Be((long)(542_974 * 10 * Close));
        rows[lateCount.Id].ValueSource.Should().Be(ValueSource.Derived);
        rows[lateValue.Id].Value.Should().Be((long)(722_279 * 10 * Close));
        rows[lateValue.Id].ValueSource.Should().Be(ValueSource.Derived);
    }

    // Splits with no straddling position fill the first batches ahead of Broadcom's.
    private async Task SeedSplitsWithoutPositions(int count)
    {
        var seed = CreateSharedContext();
        for (var i = 1; i <= count; i++)
        {
            var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
                Ticker: $"NONE{i}",
                Name: $"Issuer without positions {i}"
            );
            seed.Set<EquityIssuer>().Add(issuer);
            await seed.SaveChangesAsync();
            seed.Add(
                new StockSplit
                {
                    Id = Guid.Parse($"00000000-0000-0000-0000-{i:D12}"),
                    EquityIssuerId = issuer.Id,
                    EquityListingId = issuer.Presentation.EquityListingId,
                    PriceSeriesTicker = $"NONE{i}",
                    EffectiveDate = SplitDate,
                    Numerator = 2m,
                    Denominator = 1m,
                    Source = StockSplitSource.External,
                    PriceAdjustmentAppliedTime = new DateTime(
                        2024,
                        7,
                        16,
                        0,
                        0,
                        0,
                        DateTimeKind.Utc
                    ),
                    ShareCountEvidence = StockSplitShareCountEvidence.Confirmed,
                }
            );
        }
        await seed.SaveChangesAsync();
    }

    // The worker restarts several times a day, so a sweep that stopped after one batch per cycle
    // never reached a split past the first batch.
    [Fact]
    public async Task Repair_SplitBeyondTheFirstBatch_IsCorrectedInTheSameSweep()
    {
        var (lateCount, lateValue, _) = await SeedBroadcomQuarter(
            splitId: Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")
        );
        await SeedSplitsWithoutPositions(2);
        var service = new SplitBasisHoldingRepairService(
            CreateScopeFactory(),
            _priceProvider,
            Substitute.For<ILogger<SplitBasisHoldingRepairService>>()
        )
        {
            SplitsPerBatch = 1,
        };

        var corrected = await service.Repair(CancellationToken.None);

        corrected.Should().Be(2);
        var rows = await Reload();
        rows[lateCount.Id].Shares.Should().Be(542_974);
        rows[lateValue.Id].FiledValue.Should().Be(1_159_640_600);
    }

    // An earlier release rebased a 7-share Broadcom position filed after the 10:1 split to one
    // share. Its evidence cannot restore the voting columns or legs, so the sweep queues the
    // filing for recovery once and leaves the stored row for that replay.
    [Fact]
    public async Task Repair_CorrectionLostToRounding_QueuesItsFilingForRecoveryOnce()
    {
        await SeedBroadcomQuarter();
        var seed = CreateSharedContext();
        var issuer = await seed.Set<EquityIssuer>().SingleAsync();
        var holder = new InstitutionalHolder
        {
            Id = Guid.NewGuid(),
            Cik = "2009426",
            Name = "Seven-share filer",
        };
        var damaged = Position(
            issuer,
            holder,
            ReportDate,
            new DateOnly(2024, 8, 14),
            "0002009426-24-000003",
            1_125L,
            ValueSource.Derived,
            1L
        );
        damaged.Value = 1_605L;
        seed.Set<InstitutionalHolder>().Add(holder);
        seed.Set<InstitutionalHolding>().Add(damaged);
        seed.Add(
            new HoldingsCorrectionEvidence
            {
                CorrectionKey = SplitBasisCorrectionEvidence.SharesCorrectionKey,
                SourceTable = SplitBasisCorrectionEvidence.SourceTable,
                SourceRecordId = "0002009426-24-000003|11135F101|",
                SourceSchema = "[]",
                OriginalRow =
                    @"{""AccessionNumber"":""0002009426-24-000003"",""Cusip"":""11135F101"",""ListedTicker"":null,""ReportDate"":""2024-06-30"",""FilingDate"":""2024-08-14"",""Shares"":7,""FiledValue"":1125,""Factor"":10,""NeighbourShares"":1}",
                MigratedAt = DateTime.UtcNow,
            }
        );
        await seed.SaveChangesAsync();

        await CreateService().Repair(CancellationToken.None);
        await CreateService().Repair(CancellationToken.None);

        var db = CreateSharedContext();
        var request = await db.Set<HoldingsImportFailure>()
            .SingleAsync(f => f.AccessionNumber == "0002009426-24-000003");
        request.Cik.Should().Be("2009426");
        request.FilingDate.Should().Be(new DateOnly(2024, 8, 14));
        request.Reason.Should().Be(HoldingsImportFailureReason.PendingRecovery);
        request.ResolvedAt.Should().BeNull();
        (
            await db.Set<HoldingsCorrectionEvidence>()
                .CountAsync(e =>
                    e.CorrectionKey == SplitBasisCorrectionEvidence.RoundingReplayKey
                    && e.SourceRecordId == "0002009426-24-000003|11135F101|"
                )
        )
            .Should()
            .Be(1);
        (await Reload())[damaged.Id].Shares.Should().Be(1);
    }

    [Fact]
    public async Task Repair_SecondSweep_WritesNothingAndKeepsOneEvidenceRowPerPosition()
    {
        await SeedBroadcomQuarter();
        await CreateService().Repair(CancellationToken.None);
        var afterFirst = await Reload();

        var corrected = await CreateService().Repair(CancellationToken.None);

        corrected.Should().Be(0);
        var afterSecond = await Reload();
        afterSecond
            .Values.Select(h => (h.Id, h.Shares, h.FiledValue, h.ValuePending))
            .Should()
            .BeEquivalentTo(
                afterFirst.Values.Select(h => (h.Id, h.Shares, h.FiledValue, h.ValuePending))
            );
        var evidence = await CreateSharedContext()
            .Set<HoldingsCorrectionEvidence>()
            .AsNoTracking()
            .OrderBy(e => e.CorrectionKey)
            .ToListAsync();
        evidence
            .Select(e => (e.CorrectionKey, e.SourceTable, e.SourceRecordId))
            .Should()
            .Equal(
                (
                    SplitBasisCorrectionEvidence.SharesCorrectionKey,
                    "InstitutionalHolding",
                    "0001085146-24-003837|11135F101|"
                ),
                (
                    SplitBasisCorrectionEvidence.ValueCorrectionKey,
                    "InstitutionalHolding",
                    "0001608046-24-000005|11135F101|"
                )
            );
        evidence[0].OriginalRow.Should().Contain("\"Shares\": 5429741");
    }

    // A split stamped as price-adjusted while the closes are still as traded prices every consistent
    // holder ten times over; their filed figures must survive until the closes are fixed.
    [Fact]
    public async Task Repair_ClosesStillAsTraded_RewritesNothing()
    {
        await SeedBroadcomQuarter(close: Close * 10);
        var before = await Reload();

        var corrected = await CreateService().Repair(CancellationToken.None);

        corrected.Should().Be(0);
        var after = await Reload();
        after
            .Values.Select(h => (h.Id, h.Shares, h.FiledValue, h.Value, h.ValuePending))
            .Should()
            .BeEquivalentTo(
                before.Values.Select(h => (h.Id, h.Shares, h.FiledValue, h.Value, h.ValuePending))
            );
        (await CreateSharedContext().Set<HoldingsCorrectionEvidence>().CountAsync()).Should().Be(0);
    }

    // A replay re-stages the corrections an earlier import already recorded.
    [Fact]
    public async Task Stage_CorrectionAlreadyRecorded_AddsNothing()
    {
        var correction = new SplitBasisCorrection(
            SplitBasisShareCountRepairer.Kind.PostSplitShares,
            10m,
            "0001085146-24-003837",
            "11135F101",
            null,
            ReportDate,
            new DateOnly(2024, 8, 14),
            5_429_741L,
            870_443_781L,
            504_523L
        );
        var first = CreateSharedContext();
        (await SplitBasisCorrectionEvidence.Stage(first, [correction], CancellationToken.None))
            .Should()
            .Be(1);
        await first.SaveChangesAsync();

        var replay = CreateSharedContext();
        (
            await SplitBasisCorrectionEvidence.Stage(
                replay,
                [correction, correction],
                CancellationToken.None
            )
        )
            .Should()
            .Be(0);
        await replay.SaveChangesAsync();

        (await CreateSharedContext().Set<HoldingsCorrectionEvidence>().CountAsync()).Should().Be(1);
    }
}
