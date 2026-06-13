using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Coverage for <c>GetRecentFilings()</c>, which reads one row per filing from the
/// <see cref="InstitutionalFiling"/> rollup (not a live GROUP BY over holdings): the
/// projection must surface the rollup's counts/values, <c>MarkNewFilers</c> must flag
/// first-time filers from each filer's earliest holding report date (the bounded
/// replacement for the per-row NOT EXISTS that timed the page out, #3474), and the
/// migration's one-time backfill SQL must collapse holdings into that rollup.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InstitutionalHoldingRepositoryRecentFilingsTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<Equibles.Data.EquiblesFinancialDbContext> _contexts = [];

    public InstitutionalHoldingRepositoryRecentFilingsTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
    }

    private Equibles.Data.EquiblesFinancialDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    private static readonly DateOnly Q3 = new(2024, 9, 30);
    private static readonly DateOnly Q4 = new(2024, 12, 31);

    [Fact]
    public async Task GetRecentFilings_FilerWithNoPriorQuarter_FlaggedAsNewFiler()
    {
        // Contract: IsNewFiler is true when no holdings exist for the filer at
        // any ReportDate strictly earlier than this filing's ReportDate. A
        // returning filer (Q3 + Q4) must be false; a first-time filer (Q4 only)
        // must be true. GetRecentFilings no longer computes the flag (the per-row
        // NOT EXISTS timed the page out, #3474); MarkNewFilers sets it for the
        // materialised page from each filer's earliest holding report date, keyed
        // on the correct InstitutionalHolderId — a broken key would flag both or neither.
        await using var seed = FreshContext();
        var stock = await SeedStock(seed, "AAPL");
        var returningFiler = await SeedHolder(seed, "returning");
        var newFiler = await SeedHolder(seed, "firsttime");

        seed.Add(MakeHolding(stock, returningFiler, Q3, accession: "acc-ret-q3"));
        seed.Add(MakeHolding(stock, returningFiler, Q4, accession: "acc-ret-q4"));
        seed.Add(MakeHolding(stock, newFiler, Q4, accession: "acc-new-q4"));
        // The feed reads the rollup, so each filing needs its InstitutionalFiling row.
        seed.Add(MakeFiling(returningFiler, Q3, "acc-ret-q3"));
        seed.Add(MakeFiling(returningFiler, Q4, "acc-ret-q4"));
        seed.Add(MakeFiling(newFiler, Q4, "acc-new-q4"));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var filings = await sut.GetRecentFilings().ToListAsync();
        await sut.MarkNewFilers(filings);

        var returningFiling = filings.Single(f => f.FilerCik == "returning" && f.ReportDate == Q4);
        var newFiling = filings.Single(f => f.FilerCik == "firsttime");

        returningFiling.IsNewFiler.Should().BeFalse();
        newFiling.IsNewFiler.Should().BeTrue();
    }

    [Fact]
    public async Task GetRecentFilings_ProjectsRollupCountsAndValues()
    {
        // The feed surfaces PositionCount / TotalValue straight from the
        // InstitutionalFiling rollup, not a live count over holdings. Seed a
        // rollup row whose counts deliberately differ from the seeded holdings to
        // prove the read path uses the rollup table.
        await using var seed = FreshContext();
        var stock = await SeedStock(seed, "MSFT");
        var holder = await SeedHolder(seed, "rollup");

        // A holding exists for this accession, but the rollup independently claims 3
        // positions worth 900k — the read path must surface the rollup, not the holdings.
        seed.Add(MakeHolding(stock, holder, Q4, accession: "acc-roll"));
        seed.Add(MakeFiling(holder, Q4, "acc-roll", positionCount: 3, totalValue: 900_000));
        await seed.SaveChangesAsync();

        await using var read = FreshContext();
        var sut = new InstitutionalHoldingRepository(read);

        var filing = (await sut.GetRecentFilings().ToListAsync()).Single();

        filing.AccessionNumber.Should().Be("acc-roll");
        filing.PositionCount.Should().Be(3);
        filing.TotalValue.Should().Be(900_000);
        filing.FilerCik.Should().Be("rollup");
    }

    [Fact]
    public async Task Backfill_CollapsesHoldingsIntoOneRowPerAccession()
    {
        // Pins the one-time historical backfill shipped in the
        // AddInstitutionalFiling migration: it must collapse per-position holdings
        // into one InstitutionalFiling row per accession with COUNT / SUM matching
        // the old inline feed grouping. This statement mirrors the migration's Sql.
        await using var seed = FreshContext();
        var stockA = await SeedStock(seed, "NVDA");
        var stockB = await SeedStock(seed, "AMD");
        var filerA = await SeedHolder(seed, "filerA");
        var filerB = await SeedHolder(seed, "filerB");

        // acc-a: two positions for filerA at Q4 → count 2, value 350.
        seed.Add(MakeHolding(stockA, filerA, Q4, "acc-a", value: 100));
        seed.Add(MakeHolding(stockB, filerA, Q4, "acc-a", value: 250));
        // acc-b: one position for filerB at Q3 → count 1, value 500.
        seed.Add(MakeHolding(stockA, filerB, Q3, "acc-b", value: 500));
        await seed.SaveChangesAsync();

        await using var run = FreshContext();
        await run.Database.ExecuteSqlRawAsync(BackfillSql);

        await using var read = FreshContext();
        var filings = await read.Set<InstitutionalFiling>().ToListAsync();

        filings.Should().HaveCount(2);
        var a = filings.Single(f => f.AccessionNumber == "acc-a");
        a.PositionCount.Should().Be(2);
        a.TotalValue.Should().Be(350);
        a.ReportDate.Should().Be(Q4);
        var b = filings.Single(f => f.AccessionNumber == "acc-b");
        b.PositionCount.Should().Be(1);
        b.TotalValue.Should().Be(500);
    }

    // Mirrors the one-time backfill in 20260601235824_AddInstitutionalFiling.
    private const string BackfillSql = """
        INSERT INTO "InstitutionalFiling"
            ("Id", "AccessionNumber", "InstitutionalHolderId", "FilingDate",
             "ReportDate", "IsAmendment", "PositionCount", "TotalValue", "CreationTime")
        SELECT
            gen_random_uuid(),
            "AccessionNumber",
            "InstitutionalHolderId",
            "FilingDate",
            "ReportDate",
            "IsAmendment",
            COUNT(*),
            COALESCE(SUM("Value"), 0)::bigint,
            MIN("CreationTime")
        FROM "InstitutionalHolding"
        WHERE "AccessionNumber" IS NOT NULL
        GROUP BY "AccessionNumber", "InstitutionalHolderId", "FilingDate",
                 "ReportDate", "IsAmendment";
        """;

    private static async Task<CommonStock> SeedStock(
        Equibles.Data.EquiblesFinancialDbContext ctx,
        string ticker
    )
    {
        var stock = new CommonStock
        {
            Ticker = ticker,
            Name = $"{ticker} Test Corp.",
            Cik = $"C{Guid.NewGuid().GetHashCode() & int.MaxValue:D8}",
        };
        ctx.Add(stock);
        await ctx.SaveChangesAsync();
        return stock;
    }

    private static async Task<InstitutionalHolder> SeedHolder(
        Equibles.Data.EquiblesFinancialDbContext ctx,
        string cik
    )
    {
        var holder = new InstitutionalHolder { Cik = cik, Name = $"Holder {cik}" };
        ctx.Add(holder);
        await ctx.SaveChangesAsync();
        return holder;
    }

    private static InstitutionalHolding MakeHolding(
        CommonStock stock,
        InstitutionalHolder holder,
        DateOnly reportDate,
        string accession,
        long value = 100_000
    ) =>
        new()
        {
            CommonStockId = stock.Id,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            Shares = 100,
            Value = value,
            ShareType = ShareType.Shares,
            InvestmentDiscretion = InvestmentDiscretion.Sole,
            AccessionNumber = accession,
        };

    private static InstitutionalFiling MakeFiling(
        InstitutionalHolder holder,
        DateOnly reportDate,
        string accession,
        int positionCount = 1,
        long totalValue = 100_000,
        bool isAmendment = false
    ) =>
        new()
        {
            AccessionNumber = accession,
            InstitutionalHolderId = holder.Id,
            FilingDate = reportDate.AddDays(45),
            ReportDate = reportDate,
            IsAmendment = isAmendment,
            PositionCount = positionCount,
            TotalValue = totalValue,
        };
}
