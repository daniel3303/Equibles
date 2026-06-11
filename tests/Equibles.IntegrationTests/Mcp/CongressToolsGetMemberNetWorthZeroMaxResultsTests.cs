using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data.Models;
using Equibles.Congress.Mcp.Tools;
using Equibles.Congress.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class CongressToolsGetMemberNetWorthZeroMaxResultsTests : ParadeDbMcpTestBase
{
    public CongressToolsGetMemberNetWorthZeroMaxResultsTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // Contract: the "no electronically filed annual disclosure" message is a
    // factual claim about the member. A caller passing a nonsensical
    // maxResults (0) must never turn a member WITH disclosures into that
    // false claim — the limit clamp has to keep at least one row.
    [Fact(
        Skip = "GH-3666 — maxResults 0 passes the clamp and renders the false no-disclosure message"
    )]
    public async Task GetMemberNetWorth_ZeroMaxResults_DoesNotClaimNoDisclosures()
    {
        var member = new CongressMember
        {
            Name = "Clamped Member",
            Position = CongressPosition.Representative,
        };
        DbContext.Add(member);
        DbContext.Add(
            new CongressionalAnnualDisclosure
            {
                CongressMemberId = member.Id,
                Year = 2024,
                FiledDate = new DateOnly(2025, 5, 15),
                ReportId = "5000001",
                NetWorthMinimum = 100_000,
                NetWorthMaximum = 900_000,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var sut = new CongressTools(
            new CongressionalTradeRepository(DbContext),
            new CongressMemberRepository(DbContext),
            new CongressionalAnnualDisclosureRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<CongressTools>()
        );

        var result = await sut.GetMemberNetWorth("Clamped Member", maxResults: 0);

        result.Should().NotContain("No electronically filed annual disclosure");
        result.Should().Contain("| 2024 |");
    }
}
