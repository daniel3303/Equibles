using Equibles.Congress.Data.Models;
using Equibles.Congress.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Congress;

[Collection(ParadeDbCollection.Name)]
public class CongressMemberTermRepositoryTests : ParadeDbMcpTestBase
{
    public CongressMemberTermRepositoryTests(ParadeDbFixture fixture)
        : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        DbContext.AddRange(
            Legislator("B001305", "Budd"),
            Legislator("S001191", "Sinema"),
            Term("B001305", CongressPosition.Representative, "NC", 13, "Republican", 2021, 2023),
            Term("B001305", CongressPosition.Senator, "NC", null, "Republican", 2023, 2029),
            Term("S001191", CongressPosition.Representative, "AZ", 9, "Democrat", 2017, 2019),
            Term(
                "S001191",
                CongressPosition.Senator,
                "AZ",
                null,
                "Democrat",
                new DateOnly(2019, 1, 3),
                new DateOnly(2022, 12, 9)
            ),
            Term(
                "S001191",
                CongressPosition.Senator,
                "AZ",
                null,
                "Independent",
                new DateOnly(2022, 12, 9),
                new DateOnly(2025, 1, 3)
            )
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
    }

    [Theory]
    [InlineData(2023, 1, 2, CongressPosition.Representative)]
    [InlineData(2023, 1, 3, CongressPosition.Senator)]
    [InlineData(2021, 1, 3, CongressPosition.Representative)]
    public async Task GetTermOn_BoundaryDays_EndDateBelongsToTheSuccessor(
        int year,
        int month,
        int day,
        CongressPosition expected
    )
    {
        var terms = await new CongressMemberTermRepository(DbContext)
            .GetTermOn("B001305", new DateOnly(year, month, day))
            .ToListAsync();

        terms.Should().ContainSingle().Which.Chamber.Should().Be(expected);
    }

    [Fact]
    public async Task GetTermOn_AfterTheLastTerm_ReturnsNothing()
    {
        var terms = await new CongressMemberTermRepository(DbContext)
            .GetTermOn("S001191", new DateOnly(2025, 1, 3))
            .ToListAsync();

        terms.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLatestTerms_ReturnsEachLegislatorsMostRecentTerm()
    {
        var terms = await new CongressMemberTermRepository(DbContext)
            .GetLatestTerms(["B001305", "S001191", "X000000"])
            .ToListAsync();

        terms
            .Select(term => (term.BioguideId, term.Party, term.StartDate))
            .Should()
            .BeEquivalentTo(
                new[]
                {
                    ("B001305", "Republican", new DateOnly(2023, 1, 3)),
                    ("S001191", "Independent", new DateOnly(2022, 12, 9)),
                }
            );
    }

    [Fact]
    public async Task WithTermOnTransactionDate_PartySwitch_SplitsTradesByDateAndKeepsOneRowEach()
    {
        var sinema = new CongressMember
        {
            Name = "Kyrsten Sinema",
            BioguideId = "S001191",
            Position = CongressPosition.Senator,
        };
        var unresolved = new CongressMember
        {
            Name = "Unknown Filer",
            Position = CongressPosition.Representative,
        };
        DbContext.AddRange(sinema, unresolved);
        var beforeSwitch = Trade(sinema, new DateOnly(2022, 12, 8));
        var onSwitch = Trade(sinema, new DateOnly(2022, 12, 9));
        var afterService = Trade(sinema, new DateOnly(2025, 2, 1));
        var unknownMember = Trade(unresolved, new DateOnly(2022, 12, 9));
        DbContext.AddRange(beforeSwitch, onSwitch, afterService, unknownMember);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var rows = await new CongressMemberTermRepository(DbContext)
            .WithTermOnTransactionDate(DbContext.Set<CongressionalTrade>())
            .Select(row => new { TradeId = row.Trade.Id, Party = row.Term.Party })
            .ToListAsync();

        rows.Should().HaveCount(4);
        rows.ToDictionary(row => row.TradeId, row => row.Party)
            .Should()
            .BeEquivalentTo(
                new Dictionary<Guid, string>
                {
                    [beforeSwitch.Id] = "Democrat",
                    [onSwitch.Id] = "Independent",
                    [afterService.Id] = null,
                    [unknownMember.Id] = null,
                }
            );
    }

    private static CongressLegislator Legislator(string bioguideId, string lastName) =>
        new()
        {
            BioguideId = bioguideId,
            FirstName = "First",
            LastName = lastName,
        };

    private static CongressMemberTerm Term(
        string bioguideId,
        CongressPosition chamber,
        string state,
        short? district,
        string party,
        int startYear,
        int endYear
    ) =>
        Term(
            bioguideId,
            chamber,
            state,
            district,
            party,
            new DateOnly(startYear, 1, 3),
            new DateOnly(endYear, 1, 3)
        );

    private static CongressMemberTerm Term(
        string bioguideId,
        CongressPosition chamber,
        string state,
        short? district,
        string party,
        DateOnly start,
        DateOnly end
    ) =>
        new()
        {
            BioguideId = bioguideId,
            Chamber = chamber,
            State = state,
            District = district,
            Party = party,
            StartDate = start,
            EndDate = end,
        };

    private static CongressionalTrade Trade(CongressMember member, DateOnly transactionDate) =>
        new()
        {
            CongressMember = member,
            TransactionDate = transactionDate,
            FilingDate = transactionDate.AddDays(30),
            TransactionType = CongressTransactionType.Purchase,
            OwnerType = "",
            AssetName = $"Asset {transactionDate:O} {member.Name}",
            AmountFrom = 1_001,
            AmountTo = 15_000,
        };
}
