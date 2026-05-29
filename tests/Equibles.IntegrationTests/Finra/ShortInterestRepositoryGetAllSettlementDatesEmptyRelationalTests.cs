using Equibles.Finra.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Finra;

// On the relational provider GetAllSettlementDates runs a recursive-CTE whose anchor scalar
// subquery yields NULL when the table is empty; the `WHERE "Value" IS NOT NULL` filter must
// terminate to zero rows — not a spurious default(DateOnly) entry and not an exception.
// ShortInterestImportService calls GetAllSettlementDates().ToListAsync() directly, so a fresh
// DB (no short-interest rows yet) hits exactly this path on the first import run.
[Collection(ParadeDbCollection.Name)]
public class ShortInterestRepositoryGetAllSettlementDatesEmptyRelationalTests : ParadeDbMcpTestBase
{
    public ShortInterestRepositoryGetAllSettlementDatesEmptyRelationalTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetAllSettlementDates_OnRelationalProviderWithNoRows_ReturnsEmpty()
    {
        // The base fixture resets to an empty schema before each test — no rows seeded.
        await using var verify = Fixture.CreateDbContext();
        var sut = new ShortInterestRepository(verify);

        var dates = await sut.GetAllSettlementDates().ToListAsync();

        dates.Should().BeEmpty();
    }
}
