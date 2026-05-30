using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins the Form ADV adviser repository queries against the real ParadeDB provider, where the
/// <c>ILIKE</c> name search and the AUM ordering actually execute. A unit test cannot cover these:
/// <c>EF.Functions.ILike</c> has no in-memory translation.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FormAdvAdviserRepositoryTests : ParadeDbMcpTestBase
{
    public FormAdvAdviserRepositoryTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private async Task Seed()
    {
        DbContext
            .Set<FormAdvAdviser>()
            .AddRange(
                new FormAdvAdviser
                {
                    Crd = 100,
                    LegalName = "VANGUARD GROUP INC",
                    PrimaryBusinessName = "VANGUARD",
                    TotalRegulatoryAum = 8_000_000_000_000L,
                    ReportDate = new DateOnly(2022, 4, 1),
                },
                new FormAdvAdviser
                {
                    Crd = 200,
                    LegalName = "SMALL ADVISERS LLC",
                    PrimaryBusinessName = "SMALL VANGUARD PARTNERS",
                    TotalRegulatoryAum = 50_000_000L,
                    ReportDate = new DateOnly(2022, 4, 1),
                },
                new FormAdvAdviser
                {
                    Crd = 300,
                    LegalName = "UNRELATED CAPITAL",
                    PrimaryBusinessName = "UNRELATED",
                    TotalRegulatoryAum = 1_000_000_000L,
                    ReportDate = new DateOnly(2022, 4, 1),
                }
            );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Search_MatchesLegalOrBusinessNameCaseInsensitively_LargestAumFirst()
    {
        await Seed();
        var sut = new FormAdvAdviserRepository(DbContext);

        var results = await sut.Search("vanguard").ToListAsync();

        // Lowercase term matches the uppercase legal name (CRD 100) and the business name (CRD 200).
        results.Select(a => a.Crd).Should().Equal(100, 200);
    }

    [Fact]
    public async Task GetByCrd_ReturnsOnlyTheRequestedAdviser()
    {
        await Seed();
        var sut = new FormAdvAdviserRepository(DbContext);

        var result = await sut.GetByCrd(300).SingleAsync();

        result.LegalName.Should().Be("UNRELATED CAPITAL");
    }
}
