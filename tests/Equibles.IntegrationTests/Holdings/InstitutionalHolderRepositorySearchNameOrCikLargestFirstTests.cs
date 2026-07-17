using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using FluentAssertions;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Pins the two resolution fixes behind the MCP audit's wrong-entity findings:
/// (1) SearchNameOrCikLargestFirst ranks matches by 13F size (the InstitutionalFiling
/// rollup's TotalValue), so "Bridgewater" resolves to Bridgewater Associates — not whichever
/// small RIA has the shortest or alphabetically-first name — with rollup-less (13D/G-only)
/// filers last; (2) an all-digit query strips its leading zeros before the CIK prefix match,
/// so the SEC-canonical zero-padded '0001067983' resolves the same filer as the stored
/// unpadded '1067983'.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHolderRepositorySearchNameOrCikLargestFirstTests : ParadeDbMcpTestBase
{
    public InstitutionalHolderRepositorySearchNameOrCikLargestFirstTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task SearchNameOrCikLargestFirst_RanksTheLargest13FFilerFirst()
    {
        // The small RIA has the SHORTER name — shortest-name-wins used to pick it.
        var smallRia = new InstitutionalHolder { Cik = "1600319", Name = "Bridgewater Adv." };
        var flagship = new InstitutionalHolder
        {
            Cik = "1350694",
            Name = "Bridgewater Associates, LP",
        };
        var noFilings = new InstitutionalHolder
        {
            Cik = "1648901",
            Name = "Bridgewater Wealth LLC",
        };
        DbContext.AddRange(smallRia, flagship, noFilings);
        DbContext.AddRange(
            new InstitutionalFiling
            {
                AccessionNumber = "acc-ria",
                InstitutionalHolderId = smallRia.Id,
                FilingDate = new DateOnly(2026, 2, 14),
                ReportDate = new DateOnly(2025, 12, 31),
                PositionCount = 300,
                TotalValue = 592_929_169L,
            },
            new InstitutionalFiling
            {
                AccessionNumber = "acc-flagship",
                InstitutionalHolderId = flagship.Id,
                FilingDate = new DateOnly(2026, 2, 14),
                ReportDate = new DateOnly(2025, 12, 31),
                PositionCount = 700,
                TotalValue = 23_255_201_987L,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var repository = new InstitutionalHolderRepository(verify);

        var matches = await repository.SearchNameOrCikLargestFirst("Bridgewater", 3);

        matches.Should().HaveCount(3);
        matches[0].Name.Should().Be("Bridgewater Associates, LP");
        matches[1].Name.Should().Be("Bridgewater Adv.");
        // Filers with no 13F rollup rows rank last.
        matches[2].Name.Should().Be("Bridgewater Wealth LLC");
    }

    [Fact]
    public async Task SearchNameOrCik_ZeroPaddedCik_ResolvesTheStoredUnpaddedFiler()
    {
        var holder = new InstitutionalHolder { Cik = "1067983", Name = "Berkshire Hathaway Inc" };
        DbContext.Add(holder);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var repository = new InstitutionalHolderRepository(verify);

        var matches = await repository.SearchNameOrCikLargestFirst("0001067983", 5);

        matches.Should().ContainSingle().Which.Cik.Should().Be("1067983");
    }
}
