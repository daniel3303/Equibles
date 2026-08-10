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
        DbContext.Add(
            new CftcContract
            {
                MarketCode = "13874+",
                MarketName = "E-MINI S&P 500",
                Category = CftcContractCategory.EquityIndices,
            }
        );
        DbContext.Add(
            new CftcContract
            {
                MarketCode = "06765A",
                MarketName = "Crude Oil",
                Category = CftcContractCategory.Energy,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new CftcContractRepository(verify);

        var results = await sut.Search("13874").AsNoTracking().ToListAsync();

        results.Should().ContainSingle();
        results[0].MarketCode.Should().Be("13874+");
    }

    [Theory]
    [InlineData("ES")]
    [InlineData("S&P 500")]
    [InlineData("S&P500")]
    [InlineData("e mini s p")]
    public async Task Search_StandardContractVocabulary_ResolvesCuratedMarketCode(string query)
    {
        DbContext.AddRange(
            new CftcContract
            {
                MarketCode = "13874A",
                MarketName = "E-mini S&P 500 (CME)",
                Category = CftcContractCategory.EquityIndices,
            },
            new CftcContract
            {
                MarketCode = "ZZES",
                MarketName = "Treasury Notes",
                Category = CftcContractCategory.InterestRates,
            },
            new CftcContract
            {
                MarketCode = "DSTRCT",
                MarketName = "E-mini S&P 500 Notes",
                Category = CftcContractCategory.EquityIndices,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var results = await new CftcContractRepository(verify)
            .Search(query)
            .AsNoTracking()
            .ToListAsync();

        results.Should().ContainSingle().Which.MarketCode.Should().Be("13874A");
    }

    [Fact]
    public async Task Search_ExactStoredCode_OutranksVerifiedAlias()
    {
        DbContext.AddRange(
            new CftcContract
            {
                MarketCode = "ES",
                MarketName = "Exact stored ES contract",
                Category = CftcContractCategory.Other,
            },
            new CftcContract
            {
                MarketCode = "13874A",
                MarketName = "E-mini S&P 500 (CME)",
                Category = CftcContractCategory.EquityIndices,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var results = await new CftcContractRepository(verify)
            .Search("ES")
            .AsNoTracking()
            .ToListAsync();

        results.Should().ContainSingle().Which.MarketCode.Should().Be("ES");
    }

    [Fact]
    public async Task Search_NoAllTokenMatch_BroadensToAnyToken()
    {
        DbContext.AddRange(
            new CftcContract
            {
                MarketCode = "088691",
                MarketName = "GOLD - COMMODITY EXCHANGE INC.",
                Category = CftcContractCategory.Metals,
            },
            new CftcContract
            {
                MarketCode = "084691",
                MarketName = "SILVER - COMMODITY EXCHANGE INC.",
                Category = CftcContractCategory.Metals,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var results = await new CftcContractRepository(verify)
            .Search("front month gold")
            .AsNoTracking()
            .ToListAsync();

        results.Select(c => c.MarketCode).Should().Contain("088691");
    }
}
