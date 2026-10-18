using Equibles.CommonStocks.Repositories;
using Equibles.Congress.Data.Models;
using Equibles.Congress.Mcp.Tools;
using Equibles.Congress.Repositories;
using Equibles.IntegrationTests.Helpers;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

[Collection(ParadeDbCollection.Name)]
public class CongressToolsPartyTests : ParadeDbMcpTestBase
{
    private CongressTools Sut() =>
        new(
            new CongressionalTradeRepository(DbContext),
            new CongressMemberRepository(DbContext),
            new CongressionalAnnualDisclosureRepository(DbContext),
            new CongressMemberTermRepository(DbContext),
            new EquityIssuerRepository(DbContext),
            ErrorManager,
            NullLogger<CongressTools>()
        );

    public CongressToolsPartyTests(ParadeDbFixture fixture)
        : base(fixture) { }

    // Sinema: House Democrat, then Senate Democrat until her 2022-12-09 switch, then Independent
    // until 2025-01-03. Her latest term is Independent even though she no longer serves.
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        DbContext.AddRange(
            new CongressLegislator
            {
                BioguideId = "S001191",
                FirstName = "Kyrsten",
                LastName = "Sinema",
            },
            Term(
                CongressPosition.Representative,
                9,
                "Democrat",
                new DateOnly(2013, 1, 3),
                new DateOnly(2019, 1, 3)
            ),
            Term(
                CongressPosition.Senator,
                null,
                "Democrat",
                new DateOnly(2019, 1, 3),
                new DateOnly(2022, 12, 9)
            ),
            Term(
                CongressPosition.Senator,
                null,
                "Independent",
                new DateOnly(2022, 12, 9),
                new DateOnly(2025, 1, 3)
            )
        );
        await DbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchCongressMembers_MemberWithTerms_ReportsLatestTermParty()
    {
        DbContext.Add(Sinema());
        await DbContext.SaveChangesAsync();

        var result = await Sut().SearchCongressMembers("Sinema");

        result.Should().Contain("| Name | Position | Seat | Party |");
        RowStartingWith(result, "| Kyrsten Sinema |").Should().EndWith("| Independent |");
    }

    [Fact]
    public async Task GetMemberTrades_PartySwitch_ReportsPartyOnEachTransactionDate()
    {
        var sinema = Sinema();
        DbContext.Add(sinema);
        DbContext.AddRange(
            Trade(sinema, new DateOnly(2022, 12, 8)),
            Trade(sinema, new DateOnly(2022, 12, 9))
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetMemberTrades("Kyrsten Sinema", startDate: "2022-01-01", endDate: "2022-12-31");

        result.Should().Contain("| Account | Party |");
        RowStartingWith(result, "| 2022-12-08 |").Should().EndWith("| Democrat |");
        RowStartingWith(result, "| 2022-12-09 |").Should().EndWith("| Independent |");
    }

    [Fact]
    public async Task GetMemberTrades_TradeOutsideAnyTerm_ReportsNoParty()
    {
        var sinema = Sinema();
        DbContext.Add(sinema);
        DbContext.AddRange(
            Trade(sinema, new DateOnly(2012, 6, 1)),
            Trade(sinema, new DateOnly(2025, 1, 3)),
            Trade(sinema, new DateOnly(2025, 1, 2))
        );
        await DbContext.SaveChangesAsync();

        var result = await Sut()
            .GetMemberTrades("Kyrsten Sinema", startDate: "2012-01-01", endDate: "2025-12-31");

        // Before her first term and on her last term's (exclusive) end date there is no party.
        RowStartingWith(result, "| 2012-06-01 |").Should().EndWith("| — |");
        RowStartingWith(result, "| 2025-01-03 |").Should().EndWith("| — |");
        RowStartingWith(result, "| 2025-01-02 |").Should().EndWith("| Independent |");
    }

    [Fact]
    public async Task MemberWithoutBioguideId_ReportsNoPartyInEitherTool()
    {
        var unresolved = new CongressMember
        {
            Name = "Unresolved Filer",
            Position = CongressPosition.Representative,
        };
        DbContext.Add(unresolved);
        DbContext.Add(Trade(unresolved, new DateOnly(2022, 12, 9)));
        await DbContext.SaveChangesAsync();

        var search = await Sut().SearchCongressMembers("Unresolved");
        var trades = await Sut()
            .GetMemberTrades("Unresolved Filer", startDate: "2022-01-01", endDate: "2022-12-31");

        RowStartingWith(search, "| Unresolved Filer |").Should().EndWith("| - |");
        RowStartingWith(trades, "| 2022-12-09 |").Should().EndWith("| — |");
    }

    private static string RowStartingWith(string markdown, string prefix) =>
        markdown
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Should()
            .ContainSingle(line => line.StartsWith(prefix, StringComparison.Ordinal))
            .Which;

    private static CongressMember Sinema() =>
        new()
        {
            Name = "Kyrsten Sinema",
            BioguideId = "S001191",
            BioguideIdSource = CongressBioguideIdSource.Catalog,
            Position = CongressPosition.Senator,
        };

    private static CongressMemberTerm Term(
        CongressPosition chamber,
        short? district,
        string party,
        DateOnly start,
        DateOnly end
    ) =>
        new()
        {
            BioguideId = "S001191",
            Chamber = chamber,
            State = "AZ",
            District = district,
            Party = party,
            StartDate = start,
            EndDate = end,
        };

    private static CongressionalTrade Trade(CongressMember member, DateOnly transactionDate) =>
        new()
        {
            CongressMember = member,
            CongressMemberId = member.Id,
            FiledTicker = "NVDA",
            TransactionDate = transactionDate,
            FilingDate = transactionDate.AddDays(30),
            TransactionType = CongressTransactionType.Purchase,
            OwnerType = "",
            AssetName = $"Asset {transactionDate:O}",
            AssetType = "ST",
            AmountFrom = 1_001,
            AmountTo = 15_000,
        };
}
