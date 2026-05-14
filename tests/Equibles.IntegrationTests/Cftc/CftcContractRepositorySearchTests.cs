using Equibles.Cftc.Data.Models;
using Equibles.Cftc.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Cftc;

/// <summary>
/// Pins <see cref="CftcContractRepository.Search"/>: the production query lowercases
/// both sides and matches against EITHER MarketCode OR MarketName. Two regression
/// surfaces — (a) dropping the MarketCode branch (users typing "ES" for the S&amp;P
/// E-Mini futures contract would only match if the name happened to contain "es"),
/// and (b) reverting to case-sensitive Contains (the web search box passes literal
/// user casing).
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CftcContractRepositorySearchTests : ParadeDbMcpTestBase
{
    public CftcContractRepositorySearchTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Search_LowercaseQueryMatchesMarketCodeOnly_ReturnsContractViaMarketCodeBranch()
    {
        // Two contracts. The query "13874" matches only the first one's MarketCode.
        // The second contract's MarketName is "Crude Oil" — chosen so it cannot
        // accidentally match "13874" via the MarketName branch.
        DbContext.Add(new CftcContract
        {
            MarketCode = "13874+",
            MarketName = "E-MINI S&P 500",
            Category = CftcContractCategory.EquityIndices,
        });
        DbContext.Add(new CftcContract
        {
            MarketCode = "06765A",
            MarketName = "Crude Oil",
            Category = CftcContractCategory.Energy,
        });
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new CftcContractRepository(verify);

        var results = await sut.Search("13874").AsNoTracking().ToListAsync();

        results.Should().ContainSingle();
        results[0].MarketCode.Should().Be("13874+");
    }
}
