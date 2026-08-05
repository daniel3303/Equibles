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
    public async Task SearchNameOrCikLargestFirst_LiveFilerBeatsLargerDormantOne()
    {
        // The corporate re-registration trap: the retired CIK keeps its giant historical
        // filings, the live successor is smaller on paper — pure size ranking resolves the
        // household name to the dead entity forever ("BlackRock" answered a two-year-stale
        // portfolio this way).
        var dormant = new InstitutionalHolder { Cik = "1364742", Name = "BlackRock Inc." };
        var live = new InstitutionalHolder { Cik = "2012383", Name = "BlackRock, Inc." };
        DbContext.AddRange(dormant, live);
        DbContext.AddRange(
            new InstitutionalFiling
            {
                AccessionNumber = "acc-dormant",
                InstitutionalHolderId = dormant.Id,
                FilingDate = new DateOnly(2024, 8, 13),
                ReportDate = new DateOnly(2024, 6, 30),
                PositionCount = 3951,
                TotalValue = 4_418_304_700_000L,
            },
            new InstitutionalFiling
            {
                AccessionNumber = "acc-live",
                InstitutionalHolderId = live.Id,
                FilingDate = new DateOnly(2026, 5, 13),
                ReportDate = new DateOnly(2026, 3, 31),
                PositionCount = 4455,
                TotalValue = 2_811_213_500_000L,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var repository = new InstitutionalHolderRepository(verify);

        var matches = await repository.SearchNameOrCikLargestFirst("BlackRock", 2);

        matches.Should().HaveCount(2);
        matches[0].Cik.Should().Be("2012383", "a live filer must beat a larger dormant one");
        matches[1].Cik.Should().Be("1364742");
    }

    [Fact]
    public async Task SearchNameOrCikLargestFirst_FilingSeasonLag_DoesNotDemoteTheFlagship()
    {
        // Mid filing season the flagship has not filed the newest quarter yet while a small
        // same-named filer has. One quarter of lag must not flip the resolution — the live
        // window is a quarter plus the 45-day deadline, with slack.
        var flagship = new InstitutionalHolder
        {
            Cik = "1350694",
            Name = "Bridgewater Associates, LP",
        };
        var promptSmallFiler = new InstitutionalHolder
        {
            Cik = "1600319",
            Name = "Bridgewater Adv.",
        };
        DbContext.AddRange(flagship, promptSmallFiler);
        DbContext.AddRange(
            new InstitutionalFiling
            {
                AccessionNumber = "acc-flagship-lag",
                InstitutionalHolderId = flagship.Id,
                FilingDate = new DateOnly(2026, 2, 14),
                ReportDate = new DateOnly(2025, 12, 31),
                PositionCount = 700,
                TotalValue = 23_255_201_987L,
            },
            new InstitutionalFiling
            {
                AccessionNumber = "acc-prompt-small",
                InstitutionalHolderId = promptSmallFiler.Id,
                FilingDate = new DateOnly(2026, 4, 2),
                ReportDate = new DateOnly(2026, 3, 31),
                PositionCount = 300,
                TotalValue = 592_929_169L,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var repository = new InstitutionalHolderRepository(verify);

        var matches = await repository.SearchNameOrCikLargestFirst("Bridgewater", 2);

        matches[0]
            .Name.Should()
            .Be(
                "Bridgewater Associates, LP",
                "one quarter of filing lag keeps the flagship inside the live window"
            );
    }

    [Fact]
    public async Task SearchNameOrCikLargestFirst_SizeRanksByLatestQuarterNotAllTimePeak()
    {
        // A filer that shrank must rank by what it is now, not by the quarter it peaked in —
        // all-time-max ranking rewards a past the filer no longer has.
        var shrunk = new InstitutionalHolder { Cik = "3000001", Name = "Alpha Cap A" };
        var steady = new InstitutionalHolder { Cik = "3000002", Name = "Alpha Cap B" };
        DbContext.AddRange(shrunk, steady);
        DbContext.AddRange(
            new InstitutionalFiling
            {
                AccessionNumber = "acc-shrunk-peak",
                InstitutionalHolderId = shrunk.Id,
                FilingDate = new DateOnly(2025, 11, 14),
                ReportDate = new DateOnly(2025, 9, 30),
                PositionCount = 500,
                TotalValue = 50_000_000_000L,
            },
            new InstitutionalFiling
            {
                AccessionNumber = "acc-shrunk-now",
                InstitutionalHolderId = shrunk.Id,
                FilingDate = new DateOnly(2026, 5, 13),
                ReportDate = new DateOnly(2026, 3, 31),
                PositionCount = 100,
                TotalValue = 1_000_000_000L,
            },
            new InstitutionalFiling
            {
                AccessionNumber = "acc-steady-now",
                InstitutionalHolderId = steady.Id,
                FilingDate = new DateOnly(2026, 5, 13),
                ReportDate = new DateOnly(2026, 3, 31),
                PositionCount = 200,
                TotalValue = 5_000_000_000L,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        await using var verify = Fixture.CreateDbContext();
        var repository = new InstitutionalHolderRepository(verify);

        var matches = await repository.SearchNameOrCikLargestFirst("Alpha Cap", 2);

        matches[0].Name.Should().Be("Alpha Cap B");
        matches[1].Name.Should().Be("Alpha Cap A");
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
