using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.BusinessLogic.Ownership;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// <c>GetInstitutionBeneficialOwnership</c> lists a filer's 13D and 13G stakes with a status:
/// held stakes by default, the rest behind <c>includeHistory</c>, and a 13G-only filer resolves.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsGetInstitutionBeneficialOwnershipTests : ParadeDbMcpTestBase
{
    private static readonly DateOnly Today = new(2026, 11, 3);

    public InstitutionalHoldingsToolsGetInstitutionBeneficialOwnershipTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetInstitutionBeneficialOwnership_ListsHeldStakes_AndHistoryOnRequest()
    {
        var holder = new InstitutionalHolder
        {
            Cik = "1661897",
            Name = "Merus Global Investments, LLC",
        };
        DbContext.Add(holder);
        var active = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "ACT",
            Name: "Active Corp",
            Cik: "0000000001"
        );
        var exited = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "EXT",
            Name: "Exit Corp",
            Cik: "0000000003"
        );
        DbContext.Add(active);
        DbContext.Add(exited);
        DbContext.Add(
            Schedule(holder, active, new DateOnly(2026, 6, 8), 1_458_618, 8.6m, "g-act-1")
        );
        DbContext.Add(
            Schedule(holder, exited, new DateOnly(2025, 5, 27), 1_500_000, 6m, "g-ext-1")
        );
        DbContext.Add(
            Schedule(holder, exited, new DateOnly(2026, 5, 15), 0, 0m, "g-ext-2", amendment: true)
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var sut = Tools();

        var held = await sut.GetInstitutionBeneficialOwnership("Merus");
        held.Should()
            .Contain(
                "## Schedule 13D and 13G stakes of Merus Global Investments, LLC (CIK 1661897)"
            );
        held.Should().Contain("1 stake held at or above 5%");
        held.Should()
            .Contain(
                "| 1 | ACT | Active Corp | SC 13G | 2026-06-08 | 2026-06-18 | 1,458,618 | 8.6% | — | current |"
            );
        held.Should().NotContain("EXT");
        held.Should()
            .Contain(
                "1 stake reported below 5%, exited or on a retired security is listed with includeHistory=true."
            );

        var history = await sut.GetInstitutionBeneficialOwnership("Merus", includeHistory: true);
        history.Should().Contain("2 stakes, each on the filer's latest 13D or 13G");
        history
            .Should()
            .Contain(
                "| 2 | EXT | Exit Corp | SC 13G/A | 2026-05-15 | 2026-05-25 | 0 | 0% | -1,500,000 since 2025-05-27 | exited 2026-05-15 | g-ext-2 |"
            );

        (await sut.GetInstitutionBeneficialOwnership("Nobody Capital"))
            .Should()
            .StartWith("No match for 'Nobody Capital'");
    }

    [Fact]
    public async Task GetInstitutionBeneficialOwnership_OnlyHistory_SaysSoWithoutATable()
    {
        var holder = new InstitutionalHolder { Cik = "900", Name = "Former Holder" };
        var stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "EXT",
            Name: "Exit Corp",
            Cik: "0000000003"
        );
        DbContext.Add(holder);
        DbContext.Add(stock);
        DbContext.Add(
            Schedule(holder, stock, new DateOnly(2026, 5, 15), 0, 0m, "g-ext-2", amendment: true)
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var output = await Tools().GetInstitutionBeneficialOwnership("900");

        output
            .Should()
            .Be(
                "No current Schedule 13D or 13G stake for Former Holder (CIK 900). 1 stake reported below 5%, exited or on a retired security is listed with includeHistory=true."
            );
    }

    private InstitutionalHoldingsTools Tools()
    {
        var verify = Fixture.CreateDbContext();
        var holdings = new InstitutionalHoldingRepository(verify);
        var splits = new StockSplitRepository(verify);
        return new InstitutionalHoldingsTools(
            holdings,
            new InstitutionalHolderRepository(verify),
            new EquityIssuerRepository(verify),
            splits,
            new StockCombinedQuarterService(holdings, splits),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>(),
            holderBeneficialOwnershipReader: new HolderBeneficialOwnershipReader(
                holdings,
                new EquityIssuerRepository(verify),
                splits,
                new FixedTimeProvider(Today)
            )
        );
    }

    private static InstitutionalHolding Schedule(
        InstitutionalHolder holder,
        EquityIssuer stock,
        DateOnly eventDate,
        long shares,
        decimal percent,
        string accession,
        bool amendment = false
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            InstitutionalHolderId = holder.Id,
            ReportDate = eventDate,
            FilingDate = eventDate.AddDays(10),
            Shares = shares,
            Value = 0,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            FilingType = FilingType.Schedule13G,
            PercentOfClass = percent,
            AccessionNumber = accession,
            IsAmendment = amendment,
        };

    private sealed class FixedTimeProvider(DateOnly day) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }
}
