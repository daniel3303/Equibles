using Equibles.CommonStocks.Data;
using Equibles.Core.Configuration;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

public class FtdImportServiceTests
{
    [Fact]
    public void GetFileNames_StartDateBeforeOldestAvailable_ClampsToJune2017AndSkipsAFileForThatMonth()
    {
        // FtdImportService.GetFileNames feeds every URL the FTD scraper hits — a regression
        // here means either we miss real data (start too late, skip a month) or we burn 404s
        // hammering the wrong URL (start too early, generate 'a' for June 2017 when only 'b'
        // exists on EDGAR). Two boundary conditions in one method:
        //   (1) startDate is clamped to OldestAvailableDate (2017-06-01) — SEC publishes no
        //       FTD data before that.
        //   (2) For June 2017 specifically, only `cnsfailsYYYYMMb.zip` exists ('a' was never
        //       published); for every other month, both `a` and `b` are generated.
        // This fact passes a deliberately-too-old start date (Jan 2017) to exercise both
        // legs together: the clamp lands on June 2017, and the result for that month must be
        // only the 'b' file. July 2017 must have both — proves the special-case is scoped to
        // June only. The test stays robust against the clock by asserting on the *prefix* of
        // the returned list rather than counts that change as time advances.

        var result = FtdImportService.GetFileNames(new DateOnly(2017, 1, 1));

        // Clamped to June 2017: the first file produced must be the 'b' file for that month,
        // because the 'a' file is skipped. There is NO 'cnsfails201706a.zip' anywhere in the
        // output for this start date.
        result.Should().NotBeEmpty();
        result[0].Should().Be("cnsfails201706b.zip");
        result.Should().NotContain("cnsfails201706a.zip");

        // July 2017 — first month past the special-case — must have BOTH 'a' and 'b' in
        // that exact order, immediately after June's 'b'.
        result.Should().HaveElementAt(1, "cnsfails201707a.zip");
        result.Should().HaveElementAt(2, "cnsfails201707b.zip");
    }

    [Fact]
    public async Task Import_WhenLatestSettlementDateIsBeyondCurrentMonth_LogsUpToDateAndDoesNotCallSecEdgar()
    {
        // FtdImportService.Import is the entry point of the FTD scraper — invoked on every
        // worker tick. Before fetching anything it resolves the next unsynced month via
        // SyncDateResolver + GetFileNames, and if that resolution yields no files (i.e. we're
        // caught up) Import must short-circuit BEFORE hitting SEC EDGAR (no zip download) AND
        // before BuildTickerMap (no DB scan of CommonStock). Every existing test on this
        // service exercises the two static helpers (GetFileNames, IsRecentFtdFile); none
        // exercise the instance Import flow at all — so the entire early-exit branch
        // currently survives any regression silently.
        //
        // The risk this pins is the blast radius of an order-of-operations regression:
        //   • A refactor that swaps `var tickerMap = await BuildTickerMap(...)` and the
        //     `if (fileNames.Count == 0) return;` early-exit would compile cleanly, pass
        //     every static-method pin in this file and its UnitTests sibling, and silently
        //     start scanning every CommonStock row on every tick of a caught-up scraper —
        //     a wasted ToDictionaryAsync per tick across thousands of rows on a daily
        //     schedule.
        //   • Worse, a refactor that moves the SEC download out of the
        //     `foreach (var fileName in fileNames)` loop (or that doesn't guard against an
        //     empty fileNames list) would issue a request to sec.gov even when there's
        //     nothing to download. SEC EDGAR enforces a 10 req/s budget per User-Agent
        //     across the whole deployment and returns 429s when exceeded; burning that
        //     budget on a no-op is exactly the kind of "small per-tick, daily forever"
        //     leak that doesn't surface in any monitor until something else also gets
        //     throttled.
        //
        // Setup: seed the in-memory DB with ONE FailToDeliver row whose SettlementDate is
        // a year in the future. SyncDateResolver.Resolve returns latestDate+1 (also future),
        // GetFileNames clamps `current` to the first of that future month, the while-loop
        // condition `current <= now` fails on the first iteration, and Import takes the
        // empty-list early-exit. The assertion is two-sided:
        //   (a) ISecEdgarClient.DownloadStream was NEVER invoked — the rate-limit-burning
        //       regression surfaces here as a Received(>0) call.
        //   (b) The seed row is unchanged and no new FailToDeliver rows were written —
        //       proves the loop body never ran. Had the import attempted to "import" the
        //       future-dated row we'd see a delta.
        var dbContext = TestDbContextFactory.Create(
            new CommonStocksModuleConfiguration(),
            new SecTestModuleConfiguration()
        );

        var futureSettlementDate = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1);
        dbContext
            .Set<FailToDeliver>()
            .Add(
                new FailToDeliver
                {
                    CommonStockId = Guid.NewGuid(),
                    SettlementDate = futureSettlementDate,
                    Quantity = 0,
                    Price = 0m,
                }
            );
        await dbContext.SaveChangesAsync();

        var failToDeliverRepo = new FailToDeliverRepository(dbContext);
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(FailToDeliverRepository), failToDeliverRepo)
        );
        var errorReporter = new ErrorReporter(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<ErrorReporter>>()
        );

        var sut = new FtdImportService(
            scopeFactory,
            secEdgarClient,
            Substitute.For<ILogger<FtdImportService>>(),
            errorReporter,
            Options.Create(new WorkerOptions { TickersToSync = [] })
        );

        await sut.Import(CancellationToken.None);

        // SEC EDGAR was never hit — the early-exit ran before BuildTickerMap and before
        // the download loop. A regression that moves the DownloadStream call upstream of
        // the empty-fileNames check would flip this to a Received(1+) and fail here.
        await secEdgarClient.DidNotReceive().DownloadStream(Arg.Any<string>());

        // The seed row is intact and no new rows were persisted — the loop body never ran.
        var rows = dbContext.Set<FailToDeliver>().ToList();
        rows.Should().ContainSingle();
        rows[0].SettlementDate.Should().Be(futureSettlementDate);
    }

    [Fact]
    public void IsRecentFtdFile_FilenameForYear2000_ReturnsFalseRegardlessOfClock()
    {
        // Companion to GetFileNames coverage: IsRecentFtdFile decides whether a 404 on an
        // FTD download is "expected" (file not yet published — log INFO) or "anomalous"
        // (URL pattern may have shifted — log WARNING and report). The recency cutoff is
        // 2 months ago by wall clock, so the function is partially clock-dependent — BUT for
        // any filename old enough that the cutoff cannot reach it, the answer is `false`
        // unconditionally. Year 2000 is 20+ years before any plausible execution clock; the
        // earliest FTD data SEC actually publishes is 2017-06.
        //
        // This `[Fact]` pins three things in one shot without depending on the clock:
        //   (1) the filename guard accepts a well-formed `cnsfailsYYYYMMa.zip` length+chars,
        //   (2) the inner date math runs (year/month parse to a valid DateOnly), and
        //   (3) the recency comparison correctly classifies a very-old date as not-recent.
        // A regression that swapped `>= twoMonthsAgo` for `<= twoMonthsAgo` (the easiest
        // sign-flip mistake) would surface here: year-2000 would become "recent" and FTD
        // 404s for ancient files would start firing the WARNING + error-report path.

        var result = FtdImportService.IsRecentFtdFile("cnsfails200001a.zip");

        result.Should().BeFalse();
    }
}
