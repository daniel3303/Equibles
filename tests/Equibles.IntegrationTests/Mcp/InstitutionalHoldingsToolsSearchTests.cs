using Equibles.CommonStocks.Repositories;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// <see cref="InstitutionalHoldingsTools"/> has four MCP tools and zero existing
/// tests. Pins the simplest entry — <c>SearchInstitutions</c> — end-to-end:
/// a lowercase query must surface institutions via ILike and render the
/// MCP-formatted markdown table with CIK / City / State columns. A regression
/// that swapped the column order or dropped a column would silently break MCP
/// clients that parse the table by position.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsSearchTests : ParadeDbMcpTestBase
{
    public InstitutionalHoldingsToolsSearchTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SearchInstitutions_LowercaseQuery_RendersMarkdownTableWithCikCityState()
    {
        DbContext.Add(
            new InstitutionalHolder
            {
                Cik = "0001067983",
                Name = "Berkshire Hathaway Inc.",
                City = "Omaha",
                StateOrCountry = "NE",
            }
        );
        DbContext.Add(
            new InstitutionalHolder
            {
                Cik = "0001364742",
                Name = "BlackRock Inc.",
                City = "New York",
                StateOrCountry = "NY",
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var sut = new InstitutionalHoldingsTools(
            new InstitutionalHoldingRepository(verify),
            new InstitutionalHolderRepository(verify),
            new CommonStockRepository(verify),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );

        var output = await sut.SearchInstitutions("berkshire");

        output.Should().Contain("Berkshire Hathaway Inc.");
        output.Should().NotContain("BlackRock");
        // Pin the header order — MCP clients parse by column position.
        output.Should().Contain("| Institution | CIK | City | State/Country |");
        output.Should().Contain("0001067983");
        output.Should().Contain("Omaha");
    }
}
