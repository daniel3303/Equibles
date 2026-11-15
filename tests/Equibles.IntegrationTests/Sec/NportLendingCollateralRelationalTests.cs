using Equibles.CommonStocks.Data.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// The lending-collateral readers and the reprocess selector against real Postgres: both hinge on
/// how SQL treats a null (a schedule stored before the mark was read) and on a correlated
/// subquery the in-memory provider evaluates differently.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class NportLendingCollateralRelationalTests : ParadeDbMcpTestBase
{
    public NportLendingCollateralRelationalTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task CollateralReaders_SplitAScheduleOnTheMajorityOfEachRowsValue()
    {
        var filing = Filing("0001378872-26-000001", parserVersion: 3);
        DbContext.Add(filing);
        DbContext.AddRange(
            Holding(filing, "Unmarked", 100m, collateral: null),
            Holding(filing, "Wholly collateral", 100m, collateral: 100m),
            Holding(filing, "Mostly collateral", 100m, collateral: 60m),
            Holding(filing, "Partly collateral", 100m, collateral: 40m),
            Holding(filing, "Short, marked without an amount", -100m, collateral: 0m)
        );
        await DbContext.SaveChangesAsync();
        var repository = new NportFilingRepository(DbContext);

        var kept = await repository
            .GetHoldingsExcludingLendingCollateral(filing)
            .Select(h => h.Name)
            .ToListAsync();
        var collateral = await repository
            .GetLendingCollateral(filing)
            .Select(h => h.Name)
            .ToListAsync();

        kept.Should()
            .BeEquivalentTo("Unmarked", "Partly collateral", "Short, marked without an amount");
        collateral.Should().BeEquivalentTo("Wholly collateral", "Mostly collateral");
        // The in-memory rule a loaded row answers with is the same one.
        (await repository.GetHoldings(filing).ToListAsync())
            .Where(h => h.IsLendingCollateral)
            .Select(h => h.Name)
            .Should()
            .BeEquivalentTo(collateral);
    }

    [Fact]
    public async Task GetAwaitingReprocess_AboveTheWholeHistoryVersion_TakesOnlyAServedWholeSchedule()
    {
        EquityIssuer fund = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "KBWP",
            Name: "Property & Casualty Insurance ETF",
            Cik: "0001378872"
        );
        DbContext.Add(fund);
        var older = Filing("0001378872-26-000010", parserVersion: 2, issuer: fund);
        var served = Filing("0001378872-26-000011", parserVersion: 2, issuer: fund);
        var current = Filing("0001378872-26-000012", parserVersion: 3, issuer: fund);
        var neverRead = Filing("0001378872-26-000013", parserVersion: 0, issuer: fund);
        var narrowed = Filing("0001100663-26-000014", parserVersion: 2, seriesId: "S000004310");
        var wholeSweep = Filing("0001100663-26-000015", parserVersion: 2, seriesId: "S000004311");
        DbContext.AddRange(older, served, current, neverRead, narrowed, wholeSweep);
        DbContext.AddRange(
            Series(served, "kbwp"),
            Series(current, "current"),
            Series(narrowed, "narrowed"),
            Series(wholeSweep, "whole-sweep")
        );
        await DbContext.SaveChangesAsync();

        var awaiting = await new NportFilingRepository(DbContext)
            .GetAwaitingReprocess(["s000004311"])
            .Select(f => f.AccessionNumber)
            .ToListAsync();

        awaiting
            .Should()
            .BeEquivalentTo(
                served.AccessionNumber,
                neverRead.AccessionNumber,
                wholeSweep.AccessionNumber
            );
    }

    private static NportFiling Filing(
        string accession,
        int parserVersion,
        EquityIssuer issuer = null,
        string seriesId = null
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            EquityIssuerId = issuer?.Id,
            RegistrantCik = issuer == null ? "0001100663" : null,
            SeriesId = seriesId,
            AccessionNumber = accession,
            FilingDate = new DateOnly(2026, 10, 30),
            ReportPeriodDate = new DateOnly(2026, 8, 31),
            ReportPeriodEnd = new DateOnly(2026, 8, 31),
            ParserVersion = parserVersion,
        };

    private static FundSeries Series(NportFiling filing, string slug) =>
        new()
        {
            IdentityKey = slug,
            Slug = slug,
            SeriesId = filing.SeriesId ?? slug,
            EquityIssuerId = filing.EquityIssuerId,
            RegistrantCik = filing.RegistrantCik,
            LatestNportFilingId = filing.Id,
        };

    private static NportHolding Holding(
        NportFiling filing,
        string name,
        decimal value,
        decimal? collateral
    ) =>
        new()
        {
            NportFilingId = filing.Id,
            Name = name,
            ValueUsd = value,
            LendingCashCollateralValue = collateral,
        };
}
