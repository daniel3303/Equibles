using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.Data;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.UnitTests.Sec;

// Pins the integrity rules SharesOutstandingProvider applies to the cover-page count, each taken
// from a real production defect:
// - an IFRS filer's per-class cover-page facts live on the ifrs-full share-capital axis, not the
//   us-gaap class-of-stock axis, and must still be summed (Quantum Biopharma reported 12 shares
//   because its per-class facts were invisible and a stale consolidated fact won);
// - IsForeignPrivateIssuer keys off the SAME fact GetCurrentSharesOutstanding picks, so a
//   multi-class 20-F filer with no current consolidated fact is still recognized;
// - a cover-page count contradicted as a collapse by BOTH the issuer's previous cover-page count
//   and the same filing's balance-sheet count is a filing artifact (a dropped digit or a
//   thousands-scaled entry: Armata 36,710 vs 36.4M; FB Bancorp 161,489 vs 17.0M; PTC Therapeutics
//   8,294,933 vs 82.9M) — the provider abstains (null) so the price feed's listed-security count
//   stands, instead of poisoning market cap and short-interest ratios until the next filing.
public class SharesOutstandingProviderCoverPageIntegrityTests
{
    private const string IfrsClassAxis = "ifrs-full:ClassesOfShareCapitalAxis";

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .Options;
        var ctx = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new FinancialFactsTestModuleConfiguration(),
            }
        );
        ctx.Database.EnsureCreated();
        return ctx;
    }

    private static SharesOutstandingProvider NewProvider(EquiblesFinancialDbContext db) =>
        new(new FinancialFactRepository(db), new FinancialConceptRepository(db));

    private static CommonStock Stock(string ticker) =>
        new()
        {
            Ticker = ticker,
            Name = ticker,
            Cik = "0001000001",
        };

    private static FinancialConcept CoverPageConcept() =>
        new() { Taxonomy = FactTaxonomy.Dei, Tag = "EntityCommonStockSharesOutstanding" };

    private static FinancialConcept BalanceSheetConcept() =>
        new() { Taxonomy = FactTaxonomy.UsGaap, Tag = "CommonStockSharesOutstanding" };

    private static FinancialFact Fact(
        CommonStock stock,
        FinancialConcept concept,
        decimal value,
        DateOnly filed,
        DateOnly asOf,
        string accession,
        DocumentType form = null
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            FinancialConceptId = concept.Id,
            Unit = "shares",
            PeriodType = FactPeriodType.Instant,
            PeriodStart = asOf,
            PeriodEnd = asOf,
            Value = value,
            FiscalYear = asOf.Year,
            FiscalPeriod = SecFiscalPeriod.FullYear,
            Form = form ?? DocumentType.TenQ,
            FiledDate = filed,
            AccessionNumber = accession,
            DimensionsKey = "",
        };

    private static FinancialFact ClassFact(
        CommonStock stock,
        FinancialConcept concept,
        decimal value,
        DateOnly filed,
        DateOnly asOf,
        string accession,
        string member,
        string axis,
        DocumentType form = null
    )
    {
        var fact = Fact(stock, concept, value, filed, asOf, accession, form);
        fact.DimensionsKey = $"{axis}={member}";
        fact.Dimensions.Add(new FinancialFactDimension { Axis = axis, Member = member });
        return fact;
    }

    [Fact]
    public async Task GetCurrentSharesOutstanding_IfrsClassAxisPerClassFacts_SumsThemOverStaleConsolidated()
    {
        await using var db = NewDb();
        // Quantum Biopharma: the 2026 20-F reports the entity count only per share class on the
        // IFRS share-capital axis (Class A multiple voting 42 + Class B subordinate 3,887,729),
        // while the last consolidated cover-page fact is a stale 12 from an older filing. The
        // per-class sum must win.
        var stock = Stock("QNTM");
        var coverPage = CoverPageConcept();
        db.AddRange(stock, coverPage);
        db.Add(
            Fact(
                stock,
                coverPage,
                12m,
                new(2025, 3, 31),
                new(2024, 12, 31),
                "0001654954-25-003693",
                DocumentType.Other
            )
        );
        const string acc = "0001185185-26-001069";
        db.Add(
            ClassFact(
                stock,
                coverPage,
                42m,
                new(2026, 3, 26),
                new(2025, 12, 31),
                acc,
                "qntm:ClassAMultipleVotingMember",
                IfrsClassAxis,
                DocumentType.TwentyF
            )
        );
        db.Add(
            ClassFact(
                stock,
                coverPage,
                3_887_729m,
                new(2026, 3, 26),
                new(2025, 12, 31),
                acc,
                "qntm:ClassBSubordinateVotingMember",
                IfrsClassAxis,
                DocumentType.TwentyF
            )
        );
        await db.SaveChangesAsync();

        var shares = await NewProvider(db).GetCurrentSharesOutstanding(stock);

        shares.Should().Be(3_887_771);
    }

    [Fact]
    public async Task IsForeignPrivateIssuer_PerClassPickFromTwentyF_ReturnsTrue()
    {
        await using var db = NewDb();
        // Same shape: the pick is the per-class sum from a 20-F, while the stale consolidated
        // fact is on a non-annual form. The gate must follow the pick, not the consolidated fact,
        // or the importer treats the filer as domestic and overwrites the listed-security count.
        var stock = Stock("QNTM");
        var coverPage = CoverPageConcept();
        db.AddRange(stock, coverPage);
        db.Add(
            Fact(
                stock,
                coverPage,
                12m,
                new(2025, 3, 31),
                new(2024, 12, 31),
                "0001654954-25-003693",
                DocumentType.Other
            )
        );
        db.Add(
            ClassFact(
                stock,
                coverPage,
                3_887_729m,
                new(2026, 3, 26),
                new(2025, 12, 31),
                "0001185185-26-001069",
                "qntm:ClassBSubordinateVotingMember",
                IfrsClassAxis,
                DocumentType.TwentyF
            )
        );
        await db.SaveChangesAsync();

        var isForeign = await NewProvider(db).IsForeignPrivateIssuer(stock);

        isForeign.Should().BeTrue();
    }

    [Fact]
    public async Task GetCurrentSharesOutstanding_ThousandsScaledCoverPage_ReturnsNull()
    {
        await using var db = NewDb();
        // Armata: the latest 10-Q cover page says 36,710 — the count typed in thousands — while
        // the previous cover-page fact says 36,632,775 and the SAME filing's balance sheet says
        // 36,431,444. Both anchors contradict the collapse, so the provider must abstain.
        var stock = Stock("ARMP");
        var coverPage = CoverPageConcept();
        var balanceSheet = BalanceSheetConcept();
        db.AddRange(stock, coverPage, balanceSheet);
        db.Add(
            Fact(
                stock,
                coverPage,
                36_632_775m,
                new(2026, 3, 25),
                new(2026, 3, 20),
                "0001628280-26-011111",
                DocumentType.TenK
            )
        );
        const string acc = "0001628280-26-022222";
        db.Add(Fact(stock, coverPage, 36_710m, new(2026, 5, 13), new(2026, 5, 12), acc));
        db.Add(Fact(stock, balanceSheet, 36_431_444m, new(2026, 5, 13), new(2026, 3, 31), acc));
        await db.SaveChangesAsync();

        var shares = await NewProvider(db).GetCurrentSharesOutstanding(stock);

        shares.Should().BeNull("a cover-page count both anchors contradict is a filing artifact");
    }

    [Fact]
    public async Task GetCurrentSharesOutstanding_DroppedDigitJustUnderTenfold_ReturnsNull()
    {
        await using var db = NewDb();
        // PTC Therapeutics: the filer dropped a digit — 8,294,933 against 82,882,024 on the same
        // filing's balance sheet and 82,774,730 on the previous cover page. The discrepancy is
        // 9.99x, so a threshold of 10 would miss it; the collapse factor must catch it.
        var stock = Stock("PTCT");
        var coverPage = CoverPageConcept();
        var balanceSheet = BalanceSheetConcept();
        db.AddRange(stock, coverPage, balanceSheet);
        db.Add(
            Fact(
                stock,
                coverPage,
                82_774_730m,
                new(2026, 2, 19),
                new(2026, 2, 13),
                "0000950170-26-011111",
                DocumentType.TenK
            )
        );
        const string acc = "0000950170-26-022222";
        db.Add(Fact(stock, coverPage, 8_294_933m, new(2026, 5, 7), new(2026, 5, 1), acc));
        db.Add(Fact(stock, balanceSheet, 82_882_024m, new(2026, 5, 7), new(2026, 3, 31), acc));
        await db.SaveChangesAsync();

        var shares = await NewProvider(db).GetCurrentSharesOutstanding(stock);

        shares.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentSharesOutstanding_BalanceSheetGarbageButHistoryConfirms_KeepsCoverPageCount()
    {
        await using var db = NewDb();
        // The inverse artifact: the balance-sheet count is mis-scaled 1000x LARGE (Regeneron 2012
        // filed 93.9 BILLION) while the cover-page count is continuous with the issuer's history.
        // Only one anchor contradicts, so the cover-page count must stand — abstaining here would
        // hand a correct figure to the fallback source for no reason.
        var stock = Stock("REGN");
        var coverPage = CoverPageConcept();
        var balanceSheet = BalanceSheetConcept();
        db.AddRange(stock, coverPage, balanceSheet);
        db.Add(
            Fact(
                stock,
                coverPage,
                93_900_000m,
                new(2012, 4, 25),
                new(2012, 4, 20),
                "0000872589-12-011111"
            )
        );
        const string acc = "0000872589-12-022222";
        db.Add(Fact(stock, coverPage, 93_955_110m, new(2012, 7, 25), new(2012, 7, 20), acc));
        db.Add(Fact(stock, balanceSheet, 93_941_788_000m, new(2012, 7, 25), new(2012, 6, 30), acc));
        await db.SaveChangesAsync();

        var shares = await NewProvider(db).GetCurrentSharesOutstanding(stock);

        shares.Should().Be(93_955_110);
    }

    [Fact]
    public async Task GetCurrentSharesOutstanding_ConsistentTinyCount_IsKept()
    {
        await using var db = NewDb();
        // QVC, Inc.: a wholly-owned subsidiary whose common stock is genuinely one share held by
        // its parent — every filing agrees. A tiny count is not an artifact when the issuer's own
        // history and balance sheet state the same figure.
        var stock = Stock("QVCCQ");
        var coverPage = CoverPageConcept();
        var balanceSheet = BalanceSheetConcept();
        db.AddRange(stock, coverPage, balanceSheet);
        db.Add(
            Fact(
                stock,
                coverPage,
                1m,
                new(2026, 4, 15),
                new(2026, 4, 10),
                "0000950170-26-033333",
                DocumentType.TenK
            )
        );
        const string acc = "0000950170-26-044444";
        db.Add(Fact(stock, coverPage, 1m, new(2026, 5, 15), new(2026, 5, 12), acc));
        db.Add(Fact(stock, balanceSheet, 1m, new(2026, 5, 15), new(2026, 3, 31), acc));
        await db.SaveChangesAsync();

        var shares = await NewProvider(db).GetCurrentSharesOutstanding(stock);

        shares.Should().Be(1);
    }

    [Fact]
    public async Task GetCurrentSharesOutstanding_CollapseWithoutBalanceSheetAnchor_KeepsCoverPageCount()
    {
        await using var db = NewDb();
        // A large drop against history alone is not enough: without the same filing's
        // balance-sheet count as a second anchor it can be a genuine event (going private, a
        // reverse split), so the authoritative cover-page tag stands.
        var stock = Stock("GONE");
        var coverPage = CoverPageConcept();
        db.AddRange(stock, coverPage);
        db.Add(
            Fact(
                stock,
                coverPage,
                20_490_501m,
                new(2025, 11, 14),
                new(2025, 11, 10),
                "0001628280-25-055555"
            )
        );
        db.Add(
            Fact(
                stock,
                coverPage,
                100m,
                new(2026, 4, 15),
                new(2026, 4, 10),
                "0001628280-26-066666",
                DocumentType.TenK
            )
        );
        await db.SaveChangesAsync();

        var shares = await NewProvider(db).GetCurrentSharesOutstanding(stock);

        shares.Should().Be(100);
    }
}
