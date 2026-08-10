using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins punctuation-independent token-AND matching against both filed adviser names.
/// Runs on ParadeDB because EF.Functions.ILike has no in-memory translation.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class FormAdvAdviserRepositorySearchLiteralUnderscoreTests : ParadeDbMcpTestBase
{
    public FormAdvAdviserRepositorySearchLiteralUnderscoreTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Search_CommaSeparatedWords_MatchAcrossFiledPunctuation()
    {
        DbContext
            .Set<FormAdvAdviser>()
            .AddRange(
                new FormAdvAdviser
                {
                    Crd = 410,
                    LegalName = "GRANTHAM, MAYO, VAN OTTERLOO & CO. LLC",
                    PrimaryBusinessName = "GMO",
                    TotalRegulatoryAum = 1_000_000L,
                    ReportDate = new DateOnly(2022, 4, 1),
                },
                new FormAdvAdviser
                {
                    Crd = 420,
                    LegalName = "GRANTHAM CAPITAL LLC",
                    PrimaryBusinessName = "GRANTHAM",
                    TotalRegulatoryAum = 2_000_000L,
                    ReportDate = new DateOnly(2022, 4, 1),
                }
            );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var sut = new FormAdvAdviserRepository(DbContext);

        var results = await sut.Search("Mayo Grantham").ToListAsync();

        results.Select(a => a.Crd).Should().Equal(410);
    }
}
