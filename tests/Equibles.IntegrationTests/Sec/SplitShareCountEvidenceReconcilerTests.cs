using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Splits;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Sec;

// The verdict write is a guarded ExecuteUpdate, which the InMemory provider cannot run.
[Collection(ParadeDbCollection.Name)]
public class SplitShareCountEvidenceReconcilerTests(ParadeDbFixture fixture)
    : ParadeDbMcpTestBase(fixture)
{
    private SplitShareCountEvidenceReconciler Reconciler() =>
        new(
            new StockSplitRepository(DbContext),
            new FinancialFactRepository(DbContext),
            new FinancialConceptRepository(DbContext)
        );

    private async Task<(
        EquityIssuer Issuer,
        StockSplit SpinOff,
        StockSplit UsFactor,
        StockSplit Milan
    )> SeedHoneywell()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "HONX");
        var cover = new FinancialConcept
        {
            Taxonomy = FactTaxonomy.Dei,
            Tag = "EntityCommonStockSharesOutstanding",
            Label = "Entity Common Stock, Shares Outstanding",
        };
        DbContext.AddRange(issuer, cover);
        await DbContext.SaveChangesAsync();

        FinancialFact Count(DateOnly asOf, DateOnly filed, decimal shares) =>
            new()
            {
                EquityIssuerId = issuer.Id,
                FinancialConceptId = cover.Id,
                Unit = "shares",
                Value = shares,
                PeriodType = FactPeriodType.Instant,
                PeriodStart = asOf,
                PeriodEnd = asOf,
                FiscalYear = asOf.Year,
                FiscalPeriod = SecFiscalPeriod.Q1,
                Form = DocumentType.TenQ,
                FiledDate = filed,
                AccessionNumber = $"0000773840-{filed:yyMMdd}",
                DimensionsKey = "",
            };
        StockSplit Split(
            DateOnly effective,
            decimal numerator,
            decimal denominator,
            bool presentation
        ) =>
            new()
            {
                EquityIssuerId = issuer.Id,
                EquityListingId = presentation ? issuer.Presentation.EquityListingId : null,
                PriceSeriesTicker = presentation ? "HONX" : "1HONX",
                EffectiveDate = effective,
                Numerator = numerator,
                Denominator = denominator,
                Source = StockSplitSource.Yahoo,
            };

        var spinOff = Split(new DateOnly(2025, 10, 30), 1061m, 1000m, presentation: true);
        var usFactor = Split(new DateOnly(2026, 6, 29), 1907m, 2000m, presentation: true);
        var milan = Split(new DateOnly(2026, 6, 29), 1m, 2m, presentation: false);
        DbContext.AddRange(
            Count(new DateOnly(2025, 9, 30), new DateOnly(2025, 10, 23), 634_887_208m),
            Count(new DateOnly(2026, 1, 23), new DateOnly(2026, 2, 17), 635_675_701m),
            Count(new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 23), 633_653_119m),
            Count(new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 23), 316_940_010m),
            spinOff,
            usFactor,
            milan
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return (issuer, spinOff, usFactor, milan);
    }

    private async Task<StockSplit> Stored(StockSplit split) =>
        await DbContext.Set<StockSplit>().AsNoTracking().SingleAsync(row => row.Id == split.Id);

    [Fact]
    public async Task Reconcile_RecordsEachVerdictOnceAndAgainOnlyWhenItChanges()
    {
        var (_, spinOff, usFactor, milan) = await SeedHoneywell();

        (await Reconciler().Reconcile(CancellationToken.None)).Should().Be(3);

        (await Stored(spinOff))
            .ShareCountEvidence.Should()
            .Be(StockSplitShareCountEvidence.Contradicted);
        (await Stored(usFactor))
            .ShareCountEvidence.Should()
            .Be(StockSplitShareCountEvidence.Contradicted);
        var confirmed = await Stored(milan);
        confirmed.ShareCountEvidence.Should().Be(StockSplitShareCountEvidence.Confirmed);
        confirmed.ShareCountEvidenceAsOf.Should().Be(new DateOnly(2026, 6, 30));
        confirmed.ShareCountEvidenceTime.Should().NotBeNull();
        (await Reconciler().Reconcile(CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task SetShareCountEvidence_ARowWhoseRatioChangedSinceItWasJudged_IsLeftAlone()
    {
        var (_, _, _, milan) = await SeedHoneywell();
        var judged = await Stored(milan);
        await DbContext
            .Set<StockSplit>()
            .Where(row => row.Id == milan.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Denominator, 3m));

        var written = await new StockSplitRepository(DbContext).SetShareCountEvidence(
            judged,
            StockSplitShareCountEvidence.Confirmed,
            new DateOnly(2026, 6, 30),
            DateTime.UtcNow,
            CancellationToken.None
        );

        written.Should().Be(0);
        (await Stored(milan)).ShareCountEvidence.Should().BeNull();
    }
}
