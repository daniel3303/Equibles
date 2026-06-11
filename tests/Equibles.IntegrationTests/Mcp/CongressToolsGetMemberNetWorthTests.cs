using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data.Models;
using Equibles.Congress.Mcp.Tools;
using Equibles.Congress.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Pins the GetMemberNetWorth MCP tool: every year renders as a band with its
/// asset/liability counts (negative bounds keep their sign), and members
/// without an electronic disclosure get the "not zero net worth" explanation
/// rather than an empty table.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CongressToolsGetMemberNetWorthTests : ParadeDbMcpTestBase
{
    public CongressToolsGetMemberNetWorthTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private CongressTools Sut() =>
        new(
            new CongressionalTradeRepository(DbContext),
            new CongressMemberRepository(DbContext),
            new CongressionalAnnualDisclosureRepository(DbContext),
            new CommonStockRepository(DbContext),
            ErrorManager,
            NullLogger<CongressTools>()
        );

    [Fact]
    public async Task GetMemberNetWorth_MemberWithDisclosures_RendersBandsNewestFirst()
    {
        var member = new CongressMember
        {
            Name = "Banded Member",
            Position = CongressPosition.Representative,
        };
        DbContext.Add(member);
        DbContext.Add(
            new CongressionalAnnualDisclosure
            {
                CongressMemberId = member.Id,
                Year = 2023,
                FiledDate = new DateOnly(2024, 5, 15),
                ReportId = "7000001",
                NetWorthMinimum = -250_000,
                NetWorthMaximum = 400_000,
                Lines =
                [
                    new CongressionalDisclosureLine
                    {
                        Kind = CongressionalDisclosureLineKind.Asset,
                        Description = "Savings",
                        RangeMinimum = 250_000,
                        RangeMaximum = 900_000,
                    },
                    new CongressionalDisclosureLine
                    {
                        Kind = CongressionalDisclosureLineKind.Liability,
                        Description = "Mortgage (Bank)",
                        RangeMinimum = 500_000,
                        RangeMaximum = 500_000,
                    },
                ],
            }
        );
        DbContext.Add(
            new CongressionalAnnualDisclosure
            {
                CongressMemberId = member.Id,
                Year = 2024,
                FiledDate = new DateOnly(2025, 5, 15),
                ReportId = "7000002",
                NetWorthMinimum = 1_015_002,
                NetWorthMaximum = 4_799_999,
                Lines =
                [
                    new CongressionalDisclosureLine
                    {
                        Kind = CongressionalDisclosureLineKind.Asset,
                        Description = "Apple Inc. (AAPL)",
                        RangeMinimum = 1_000_001,
                        RangeMaximum = 5_000_000,
                    },
                ],
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var result = await Sut().GetMemberNetWorth("Banded Member");

        result.Should().Contain("Net worth of Banded Member (Representative)");
        var year2024 = result.IndexOf("| 2024 |", StringComparison.Ordinal);
        var year2023 = result.IndexOf("| 2023 |", StringComparison.Ordinal);
        year2024.Should().BeGreaterThan(-1);
        year2023.Should().BeGreaterThan(year2024, "years render newest first");
        result.Should().Contain("| 2024 | 2025-05-15 | $1,015,002 | $4,799,999 | 1 | 0 |");
        result
            .Should()
            .Contain(
                "| 2023 | 2024-05-15 | -$250,000 | $400,000 | 1 | 1 |",
                "negative bounds keep their sign"
            );
    }

    [Fact]
    public async Task GetMemberNetWorth_MemberWithoutDisclosures_ExplainsMissingIsNotZero()
    {
        var member = new CongressMember
        {
            Name = "Paper Filer",
            Position = CongressPosition.Senator,
        };
        DbContext.Add(member);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var result = await Sut().GetMemberNetWorth("Paper Filer");

        result.Should().Contain("No electronically filed annual disclosure");
        result.Should().Contain("does not mean zero net worth");
    }
}
