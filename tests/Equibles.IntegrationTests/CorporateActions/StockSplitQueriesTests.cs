using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.CorporateActions;

// The split rows are resolved after the scope that loaded them is gone (the 13F import builds
// its split map in its own scope), and the resolver reads the listing of every post-date split,
// so the listing has to be loaded with the row. Lazy-loading proxies are on in this fixture, as
// in production, which is what makes the negative control throw.
[Collection(ParadeDbCollection.Name)]
public class StockSplitQueriesTests(ParadeDbFixture fixture) : ParadeDbMcpTestBase(fixture)
{
    private static readonly DateOnly ReportDate = new(2026, 6, 30);

    [Fact]
    public void ForIssuers_JoinsTheListing()
    {
        var sql = StockSplitQueries.ForIssuers(DbContext, [Guid.NewGuid()]).ToQueryString();

        sql.Should().Contain("\"EquityListing\"");
    }

    [Fact]
    public async Task ForIssuers_ResolvesAListedPostReportSplitAfterTheLoadingContextIsDisposed()
    {
        var issuerId = await SeedIssuerWithListedSplit();

        List<StockSplit> splits;
        using (var loader = Fixture.CreateDbContext())
        {
            splits = await StockSplitQueries.ForIssuers(loader, [issuerId]).ToListAsync();
        }

        SplitBasisResolver
            .TryResolveFactor(ReportDate, splits, null, "LAZY", [], out var factor)
            .Should()
            .BeTrue();
        factor.Should().Be(1m / 50m);
    }

    // Negative control: without the include, the same shape lazy-loads on the disposed context
    // and throws, which is the production failure the helper exists to prevent.
    [Fact]
    public async Task RawLoad_LazyLoadsTheListingOnTheDisposedContextAndThrows()
    {
        var issuerId = await SeedIssuerWithListedSplit();

        List<StockSplit> splits;
        using (var loader = Fixture.CreateDbContext())
        {
            splits = await loader
                .Set<StockSplit>()
                .Where(split => split.EquityIssuerId == issuerId)
                .ToListAsync();
        }

        var resolve = () =>
            SplitBasisResolver.TryResolveFactor(ReportDate, splits, null, "LAZY", [], out _);

        resolve.Should().Throw<InvalidOperationException>().WithMessage("*disposed*");
    }

    private async Task<Guid> SeedIssuerWithListedSplit()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "LAZY");
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();
        DbContext.Add(
            new StockSplit
            {
                EquityIssuerId = issuer.Id,
                EquityListingId = issuer.Presentation.EquityListingId,
                PriceSeriesTicker = "LAZY",
                EffectiveDate = new DateOnly(2026, 8, 3),
                Numerator = 1m,
                Denominator = 50m,
                Source = StockSplitSource.External,
                PriceAdjustmentAppliedTime = new DateTime(2026, 8, 5, 0, 0, 0, DateTimeKind.Utc),
            }
        );
        await DbContext.SaveChangesAsync();
        return issuer.Id;
    }
}
