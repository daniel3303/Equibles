using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Repositories;
using Equibles.Holdings.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Mcp.Tools;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Mcp;

/// <summary>
/// Holder entity types from stored profiles reach GetTopHolders, SearchInstitutions and
/// GetInstitutionSummary on Postgres, and the type filters run in SQL where the search pages.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingsToolsHolderTypeTests : ParadeDbMcpTestBase
{
    private static readonly DateOnly ReportDate = new(2024, 12, 31);

    public InstitutionalHoldingsToolsHolderTypeTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task GetTopHolders_ShowsEachHoldersTypeAndFiltersByIt()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple Inc.",
            Cik: "0000320193"
        );
        var adviser = Holder("1", "Adviser Capital", HolderEntityType.RegisteredAdviser);
        var bank = Holder("2", "Trust Bank", HolderEntityType.Bank);
        var unprofiled = Holder("3", "New Filer LLC", null);
        DbContext.Add(stock);
        DbContext.AddRange(
            Holding(stock, adviser, 6_000),
            Holding(stock, bank, 3_000),
            Holding(stock, unprofiled, 1_000)
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        await using var verify = Fixture.CreateDbContext();

        var all = await Sut(verify).GetTopHolders("AAPL");
        var banks = await Sut(verify).GetTopHolders("AAPL", holderType: "bank,unk");
        var refused = await Sut(verify).GetTopHolders("AAPL", holderType: "hedge");

        all.Should()
            .Contain("| 1 | Adviser Capital | Common | 6,000 | 0.6 | 60.00% | Registered adviser |")
            .And.Contain("| Trust Bank | Common | 3,000 | 0.3 | 30.00% | Bank |")
            .And.Contain("| New Filer LLC | Common | 1,000 | 0.1 | 10.00% | — |");
        banks
            .Should()
            .Contain("Showing rows 1-2 of 2 from holder type bank, unk, across 2 institutions.")
            .And.Contain("| 1 | Trust Bank |")
            .And.Contain("| 2 | New Filer LLC |")
            .And.Contain("30.00%")
            .And.NotContain("Adviser Capital");
        refused.Should().StartWith("Unknown holder type 'hedge'.");
    }

    [Fact]
    public async Task SearchInstitutions_TypeFilterRunsInTheSearchQuery()
    {
        Holder("10", "Harbor Advisers LP", HolderEntityType.RegisteredAdviser);
        Holder("11", "Harbor Bank", HolderEntityType.Bank);
        Holder("12", "Harbor Partners", null);
        Holder("13", "Harbor Holdings", HolderEntityType.Unknown);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        await using var verify = Fixture.CreateDbContext();

        var all = await Sut(verify).SearchInstitutions("harbor");
        var unknown = await Sut(verify).SearchInstitutions("harbor", type: "unk");

        all.Should()
            .Contain(
                "| Institution | CIK | Latest Report | Tracked 13F Value | Positions | City | State/Country | Holder Type |"
            )
            .And.Contain("| Bank |")
            .And.Contain("| Registered adviser |");
        unknown
            .Should()
            .Contain("Harbor Partners")
            .And.Contain("Harbor Holdings")
            .And.NotContain("Harbor Bank")
            .And.NotContain("Harbor Advisers");
    }

    [Fact]
    public async Task GetInstitutionSummary_ShowsTheProfileAndTheCikItContinues()
    {
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "AAPL",
            Name: "Apple Inc.",
            Cik: "0000320193"
        );
        var old = Holder("1364742", "BlackRock Inc. (old)", null);
        var renamed = Holder(
            "2012383",
            "BlackRock Inc.",
            HolderEntityType.RegisteredAdviser,
            conflicting: true
        );
        DbContext.Add(stock);
        DbContext.Add(Holding(stock, renamed, 1_000));
        DbContext.Add(
            new HolderSuccession
            {
                PredecessorHolderId = old.Id,
                SuccessorHolderId = renamed.Id,
                PredecessorLastReportDate = new DateOnly(2024, 6, 30),
                SuccessorFirstReportDate = new DateOnly(2024, 9, 30),
                Basis = HolderSuccessionBasis.SameIncludedManagers,
                PredecessorGroupValue = 1,
                SuccessorGroupValue = 1,
                DerivationVersion = 1,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        await using var verify = Fixture.CreateDbContext();

        var output = await Sut(verify).GetInstitutionSummary("2012383");

        output
            .Should()
            .Contain("**Holder profile** (regulatory records, never the name):")
            .And.Contain(
                "| Holder type | Registered investment adviser (Form ADV roster, CRD 9999, as of 2026-10-01) |"
            )
            .And.Contain(
                "| Conflicting evidence | Current records imply more than one type; the first in precedence decides |"
            )
            .And.Contain(
                "| Form ADV adviser | CRD 9999, as of 2026-10-01, $5,000,000 regulatory assets |"
            )
            .And.Contain(
                "| Client base | Pooled investment vehicles (62.5% of regulatory assets) |"
            )
            .And.Contain("| Private funds | 3 funds, mostly hedge funds, $900 gross assets |")
            .And.Contain(
                "| Earlier CIK | BlackRock Inc. (old) (1364742), last 13F 2024-06-30, continued here from 2024-09-30 |"
            );
    }

    private InstitutionalHolder Holder(
        string cik,
        string name,
        HolderEntityType? type,
        bool conflicting = false
    )
    {
        var holder = new InstitutionalHolder { Cik = cik, Name = name };
        DbContext.Add(holder);
        if (type is { } entityType)
            DbContext.Add(
                new InstitutionalHolderProfile
                {
                    InstitutionalHolderId = holder.Id,
                    EntityType = entityType,
                    EntityTypeSource = HolderEvidenceSource.FormAdvRoster,
                    EntityTypeSourceKey = "9999",
                    EntityTypeAsOf = new DateOnly(2026, 10, 1),
                    HasConflictingEvidence = conflicting,
                    AdviserCrd = 9999,
                    AdviserAsOf = new DateOnly(2026, 10, 1),
                    RegulatoryAum = 5_000_000,
                    ClientBase = HolderClientBase.PooledInvestmentVehicles,
                    ClientBaseAumShare = 0.625m,
                    AdvisesPrivateFunds = true,
                    PrivateFundCount = 3,
                    PrivateFundFocus = PrivateFundFocus.Hedge,
                    PrivateFundGrossAssets = 900,
                    DerivationVersion = 1,
                }
            );
        return holder;
    }

    private static InstitutionalHolding Holding(
        EquityIssuer stock,
        InstitutionalHolder holder,
        long shares
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = ReportDate.AddDays(45),
            ReportDate = ReportDate,
            Shares = shares,
            Value = shares * 100,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = $"acc-{holder.Cik}",
        };

    private InstitutionalHoldingsTools Sut(Equibles.Data.EquiblesFinancialDbContext db) =>
        new(
            new InstitutionalHoldingRepository(db),
            new InstitutionalHolderRepository(db),
            new EquityIssuerRepository(db),
            new StockSplitRepository(db),
            new StockCombinedQuarterService(
                new InstitutionalHoldingRepository(db),
                new StockSplitRepository(db)
            ),
            ErrorManager,
            Substitute.For<ILogger<InstitutionalHoldingsTools>>()
        );
}
