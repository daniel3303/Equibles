using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Adversarial guard on <see cref="FormAdvAdviserRepository.Search"/>'s "name CONTAINS term"
/// contract for the literal-underscore case. A term with an underscore must match a name that
/// contains that literal underscore (the escape must not break legitimate matches) AND must
/// exclude a name that only matches when "_" is honoured as a LIKE single-character wildcard.
/// Runs on ParadeDB because EF.Functions.ILike has no in-memory translation.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FormAdvAdviserRepositorySearchLiteralUnderscoreTests : ParadeDbMcpTestBase
{
    public FormAdvAdviserRepositorySearchLiteralUnderscoreTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Search_TermWithUnderscore_MatchesLiteralUnderscoreOnly()
    {
        DbContext
            .Set<FormAdvAdviser>()
            .AddRange(
                new FormAdvAdviser
                {
                    Crd = 410,
                    LegalName = "A_B CAPITAL",
                    PrimaryBusinessName = "A_B",
                    TotalRegulatoryAum = 1_000_000L,
                    ReportDate = new DateOnly(2022, 4, 1),
                },
                new FormAdvAdviser
                {
                    Crd = 420,
                    LegalName = "AXB CAPITAL",
                    PrimaryBusinessName = "AXB",
                    TotalRegulatoryAum = 2_000_000L,
                    ReportDate = new DateOnly(2022, 4, 1),
                }
            );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var sut = new FormAdvAdviserRepository(DbContext);

        var results = await sut.Search("A_B").ToListAsync();

        // Contract is literal containment: the underscore matches only the name that literally
        // contains it (CRD 410), never the wildcard-only match "AXB CAPITAL" (CRD 420).
        results.Select(a => a.Crd).Should().Equal(410);
    }
}
