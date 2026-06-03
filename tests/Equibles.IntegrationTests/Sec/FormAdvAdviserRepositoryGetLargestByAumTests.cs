using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

// Lane A (adversarial): GetLargestByAum promises "largest AUM first". An adviser
// whose TotalRegulatoryAum is null (unreported) must rank LAST — treated as the
// smallest, not surfaced at the top. If the null AUM weren't coalesced to a floor,
// a DESC ordering could surface unknown-AUM advisers ahead of real megafunds.
[Collection(ParadeDbCollection.Name)]
public class FormAdvAdviserRepositoryGetLargestByAumTests : ParadeDbMcpTestBase
{
    public FormAdvAdviserRepositoryGetLargestByAumTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetLargestByAum_NullAumAdviser_RanksLastBehindReportedAum()
    {
        DbContext
            .Set<FormAdvAdviser>()
            .AddRange(
                new FormAdvAdviser
                {
                    Crd = 1,
                    LegalName = "MEGA FUND",
                    TotalRegulatoryAum = 8_000_000_000_000L,
                    ReportDate = new DateOnly(2022, 4, 1),
                },
                new FormAdvAdviser
                {
                    Crd = 2,
                    LegalName = "UNKNOWN AUM ADVISER",
                    TotalRegulatoryAum = null,
                    ReportDate = new DateOnly(2022, 4, 1),
                },
                new FormAdvAdviser
                {
                    Crd = 3,
                    LegalName = "SMALL SHOP",
                    TotalRegulatoryAum = 50_000_000L,
                    ReportDate = new DateOnly(2022, 4, 1),
                }
            );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var sut = new FormAdvAdviserRepository(DbContext);

        var ordered = await sut.GetLargestByAum().Select(a => a.LegalName).ToListAsync();

        ordered.Should().Equal("MEGA FUND", "SMALL SHOP", "UNKNOWN AUM ADVISER");
    }
}
