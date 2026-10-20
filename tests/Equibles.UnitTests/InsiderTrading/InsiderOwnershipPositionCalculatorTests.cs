using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.CorporateActions.Data;
using Equibles.CorporateActions.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Data;
using Equibles.Errors.Repositories;
using Equibles.InsiderTrading.Data;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Mcp.Tools;
using Equibles.InsiderTrading.Repositories;
using Equibles.Media.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.InsiderTrading;

// GetInsiderOwnership and the unified holder list read one calculator, so the tool's answer must
// stay exactly what it printed before the calculation moved out of it.
public class InsiderOwnershipPositionCalculatorTests
{
    private static readonly DateOnly SplitDate = new(2024, 6, 10);

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new CorporateActionsModuleConfiguration(),
                new InsiderTradingModuleConfiguration(),
                new ErrorsModuleConfiguration(),
                new MediaModuleConfiguration(),
            }
        );
        db.Database.EnsureCreated();
        return db;
    }

    private static InsiderTransaction Row(
        EquityIssuer stock,
        InsiderOwner owner,
        DateOnly date,
        long ownedAfter,
        string accession,
        int order = 0,
        OwnershipNature nature = OwnershipNature.Direct,
        string title = "Common Stock",
        TransactionCode code = TransactionCode.Sale,
        InsiderSecurityKind kind = InsiderSecurityKind.NonDerivative,
        long shares = 100,
        AcquiredDisposed direction = AcquiredDisposed.Disposed
    ) =>
        new()
        {
            EquityIssuerId = stock.Id,
            InsiderOwnerId = owner.Id,
            InsiderOwner = owner,
            TransactionDate = date,
            FilingDate = date.AddDays(2),
            TransactionCode = code,
            Shares = shares,
            PricePerShare = 50m,
            AcquiredDisposed = direction,
            SharesOwnedAfter = ownedAfter,
            OwnershipNature = nature,
            SecurityTitle = title,
            AccessionNumber = accession,
            TransactionOrder = order,
            SecurityKind = kind,
        };

    // Four insiders: a multi-bucket filing, a pre-split filing, a filing without an accession and
    // a former insider at zero, plus derivative rows that never carry the balance.
    private static async Task<EquityIssuer> Seed(EquiblesFinancialDbContext db)
    {
        var stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "NVDA",
            Name: "NVIDIA Corp.",
            Cik: "0001045810"
        );
        var founder = new InsiderOwner
        {
            OwnerCik = "0001197647",
            Name = "Founder Holder",
            IsDirector = true,
            IsOfficer = true,
            OfficerTitle = "President and CEO",
        };
        var early = new InsiderOwner
        {
            OwnerCik = "0001000001",
            Name = "Early Director",
            IsDirector = true,
        };
        var noAccession = new InsiderOwner
        {
            OwnerCik = "0001000002",
            Name = "Paper Filer",
            IsTenPercentOwner = true,
        };
        var former = new InsiderOwner
        {
            OwnerCik = "0001000003",
            Name = "Former Officer",
            IsOfficer = true,
            OfficerTitle = "EVP",
        };
        db.AddRange(stock, founder, early, noAccession, former);
        db.Add(
            new StockSplit
            {
                EquityIssuerId = stock.Id,
                PriceSeriesTicker = stock.Presentation.Listing.Ticker,
                EffectiveDate = SplitDate,
                Numerator = 10,
                Denominator = 1,
                Source = StockSplitSource.Yahoo,
            }
        );
        db.AddRange(
            Row(stock, founder, new DateOnly(2025, 3, 1), 900, "acc-f-old"),
            Row(stock, founder, new DateOnly(2025, 9, 2), 5_000, "acc-f-new", order: 0),
            Row(stock, founder, new DateOnly(2025, 9, 2), 4_000, "acc-f-new", order: 1),
            Row(
                stock,
                founder,
                new DateOnly(2025, 9, 2),
                2_500,
                "acc-f-new",
                order: 2,
                nature: OwnershipNature.Indirect,
                title: "Common Stock (by trust)"
            ),
            Row(
                stock,
                founder,
                new DateOnly(2025, 9, 2),
                7_000,
                "acc-f-new",
                order: 3,
                nature: OwnershipNature.Indirect,
                title: "Common Stock (by LLC)"
            ),
            Row(
                stock,
                founder,
                new DateOnly(2025, 9, 3),
                1_234_567,
                "acc-f-deriv",
                kind: InsiderSecurityKind.Derivative
            ),
            Row(
                stock,
                early,
                new DateOnly(2023, 2, 1),
                600,
                "acc-e-1",
                code: TransactionCode.Purchase
            ),
            Row(stock, noAccession, new DateOnly(2024, 1, 5), 3_000, null),
            Row(stock, former, new DateOnly(2022, 5, 5), 0, "acc-x-1")
        );
        await db.SaveChangesAsync();
        return stock;
    }

    private static InsiderTradingTools Tools(EquiblesFinancialDbContext db) =>
        new(
            new InsiderTransactionRepository(db),
            new InsiderOwnerRepository(db),
            new Form144FilingRepository(db),
            new EquityIssuerRepository(db),
            new StockSplitRepository(db),
            new ErrorManager(new ErrorRepository(db)),
            Substitute.For<ILogger<InsiderTradingTools>>()
        );

    [Fact]
    public async Task GetInsiderOwnership_PrintsTheSameAnswerAfterTheExtraction()
    {
        await using var db = NewDb();
        await Seed(db);

        var tools = Tools(db);
        var output =
            await tools.GetInsiderOwnership("NVDA")
            + "\n====\n"
            + await tools.GetInsiderOwnership("NVDA", maxResults: 2, offset: 1);

        output.Should().Be(GoldenOutput);
    }

    [Fact]
    public async Task GetPreviousPositions_ReadsEachInsidersFilingBeforeTheCurrentOne()
    {
        await using var db = NewDb();
        var stock = await Seed(db);
        var splits = await db.Set<StockSplit>().ToListAsync();
        var calculator = new InsiderOwnershipPositionCalculator(
            new InsiderTransactionRepository(db)
        );

        var current = await calculator.GetCurrentPositions(stock, splits);
        var previous = await calculator.GetPreviousPositions(stock, current, splits);

        // Only the founder filed before the current filing; a single filing, a paper filing
        // without an accession and the derivative-only filing have no previous position.
        var founder = current.Single(p => p.Anchor.InsiderOwner.Name == "Founder Holder");
        founder.Anchor.AccessionNumber.Should().Be("acc-f-new");
        previous.Keys.Should().BeEquivalentTo([founder.Anchor.InsiderOwnerId]);
        previous[founder.Anchor.InsiderOwnerId].Anchor.AccessionNumber.Should().Be("acc-f-old");
        previous[founder.Anchor.InsiderOwnerId].TotalShares.Should().Be(900);
    }

    [Fact]
    public void Build_SeveralBalancesOnOneIndirectLine_AreAmbiguous()
    {
        // A filing keeps one balance per indirect vehicle without naming it, so two balances
        // on one indirect line may be two trusts and the closing balance compares with nothing.
        var stock = Equibles.TestSupport.EquityIssuerSeed.Create(Ticker: "NVDA");
        var owner = new InsiderOwner { OwnerCik = "0001197649", Name = "Founder" };
        var date = new DateOnly(2026, 9, 16);
        var trusts = new List<InsiderTransaction>
        {
            Row(stock, owner, date, 70_000, "acc-1", order: 0),
            Row(stock, owner, date, 31_000, "acc-1", order: 1, nature: OwnershipNature.Indirect),
            Row(stock, owner, date, 30_000, "acc-1", order: 2, nature: OwnershipNature.Indirect),
        };
        var sequentialDirect = new List<InsiderTransaction>
        {
            Row(stock, owner, date, 70_000, "acc-2", order: 0),
            Row(stock, owner, date, 69_000, "acc-2", order: 1),
            Row(stock, owner, date, 5_000, "acc-2", order: 2, nature: OwnershipNature.Indirect),
        };

        InsiderOwnershipPositionCalculator
            .Build(trusts, [])
            .IndirectBalancesAmbiguous.Should()
            .BeTrue();
        InsiderOwnershipPositionCalculator
            .Build(sequentialDirect, [])
            .IndirectBalancesAmbiguous.Should()
            .BeFalse();
    }

    private const string GoldenOutput = """
Insider ownership summary for NVIDIA Corp. (NVDA):
Showing 4 insiders with most recent data
_Each row is as-of that insider's most recent filing: the filing's closing balance per security, held directly or through each indirect vehicle it names (actual shares only), summed into Direct and Indirect and restated onto today's split basis. A vehicle the newest filing does not list is not counted. Former insiders may linger with stale dates or zero shares._

| Insider | Role | Direct Shares | Indirect Shares | Total | Last Transaction | Last Date |
|---------|------|--------------|-----------------|-------|-----------------|-----------|
| Paper Filer | 10% Owner | 30,000 | 0 | 30,000 | Sale | 2024-01-05 |
| Founder Holder | Director, President and CEO | 4,000 | 9,500 | 13,500 | Sale | 2025-09-02 |
| Early Director | Director | 6,000 | 0 | 6,000 | Purchase | 2023-02-01 |
| Former Officer | EVP | 0 | 0 | 0 | Sale | 2022-05-05 |

====
Insider ownership summary for NVIDIA Corp. (NVDA):
Showing 2 insiders with most recent data
_Each row is as-of that insider's most recent filing: the filing's closing balance per security, held directly or through each indirect vehicle it names (actual shares only), summed into Direct and Indirect and restated onto today's split basis. A vehicle the newest filing does not list is not counted. Former insiders may linger with stale dates or zero shares._

| Insider | Role | Direct Shares | Indirect Shares | Total | Last Transaction | Last Date |
|---------|------|--------------|-----------------|-------|-----------------|-----------|
| Founder Holder | Director, President and CEO | 4,000 | 9,500 | 13,500 | Sale | 2025-09-02 |
| Early Director | Director | 6,000 | 0 | 6,000 | Purchase | 2023-02-01 |

_Showing results 2-3 of 4 - raise maxResults (max 500) or pass offset=3 to continue._

""";
}
