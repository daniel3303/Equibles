using System.Reflection;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// The unmapped-CUSIP queue survives being written by several imports. One report date's filings
/// are spread across SEVERAL data sets — filing windows straddle quarter boundaries and amendments
/// land months later — so a flush that replaced the whole report-date slice let the last data set
/// processed wipe every other one's rows: Scion's $13.1M Bruker preferred vanished behind a later
/// data set's $3.3M sighting of the same identifier. Each import may only replace the keys it
/// actually saw, and may clear rows whose identifier it can now resolve.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsImportServiceFlushUnmappedCusipsTests : IAsyncLifetime
{
    private static readonly DateOnly Quarter = new(2025, 9, 30);

    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public HoldingsImportServiceFlushUnmappedCusipsTests(ParadeDbFixture fixture)
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

    [Fact]
    public async Task Flush_TwoImportsCoverTheSameQuarter_TheSecondKeepsTheFirstsRows()
    {
        // The Bruker regression. Import A (the filing window's own data set) parks Scion's
        // preferred; import B (the next window, carrying stragglers and amendments for the same
        // quarter) parks a different identifier. B must add to the queue, not restate it.
        await Flush(Context(tally: [("116794207", "BRUKER CORP", 13_137_181m)]));
        await Flush(Context(tally: [("999999999", "SOME OTHER CO", 3_261_600m)]));

        var rows = await Rows();
        rows.Should().HaveCount(2);
        rows.Single(r => r.Cusip == "116794207").FiledValue.Should().Be(13_137_181L);
        rows.Single(r => r.Cusip == "999999999").FiledValue.Should().Be(3_261_600L);
    }

    [Fact]
    public async Task Flush_SameKeySeenByALaterImport_ReplacesRatherThanDuplicatesOrSums()
    {
        // Re-importing a data set (or an amendment restating a position) must not inflate the
        // queue: the unique (Cusip, ReportDate) key is real, and the newer sighting's figures win.
        await Flush(Context(tally: [("116794207", "BRUKER CORP", 13_137_181m)]));
        await Flush(Context(tally: [("116794207", "BRUKER CORP", 14_000_000m)]));

        var row = (await Rows()).Should().ContainSingle().Subject;
        row.FiledValue.Should().Be(14_000_000L);
    }

    [Fact]
    public async Task Flush_IdentifierNowResolves_ItsRowsAreClearedEvenWithNothingToPark()
    {
        // The healing path: an operator adds the missing alias, the forced re-import runs, and the
        // once-unmapped identifier lands in CusipMapping instead of the tally. Its queue rows are
        // stale gaps and must go — including when the import parks nothing else at all, which is
        // exactly the shape of a re-import after the last gap is mapped.
        await Flush(Context(tally: [("116794207", "BRUKER CORP", 13_137_181m)]));
        await Flush(Context(tally: [], mapped: ["116794207"]));

        (await Rows()).Should().BeEmpty();
    }

    [Fact]
    public async Task Flush_ResolvedClearIsScopedToTheImportsOwnQuarters_OtherQuartersWait()
    {
        // The clear rides each import and is bounded to the quarters that import covers, so one
        // import cannot blast rows it knows nothing about; other quarters are healed when their
        // own data sets re-run (the forced re-import covers all of them).
        await Flush(
            Context(
                tally: [("116794207", "BRUKER CORP", 13_137_181m)],
                quarter: new DateOnly(2025, 6, 30)
            )
        );
        await Flush(Context(tally: [], mapped: ["116794207"], quarter: Quarter));

        var row = (await Rows()).Should().ContainSingle().Subject;
        row.ReportDate.Should().Be(new DateOnly(2025, 6, 30));
    }

    private async Task Flush(ImportContext context)
    {
        var service = new HoldingsImportService(
            CreateScopeFactory(),
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            Substitute.For<IStockPriceProvider>(),
            Substitute.For<MassTransit.IBus>()
        );

        var method = typeof(HoldingsImportService).GetMethod(
            "FlushUnmappedCusips",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        method.Should().NotBeNull();
        await (Task)method.Invoke(service, [context, CancellationToken.None]);
    }

    private static ImportContext Context(
        (string Cusip, string Issuer, decimal Dollars)[] tally,
        string[] mapped = null,
        DateOnly? quarter = null
    )
    {
        var reportDate = quarter ?? Quarter;
        var context = new ImportContext
        {
            Submissions = new Dictionary<string, SubmissionRow>(StringComparer.OrdinalIgnoreCase)
            {
                ["0000000000-25-000001"] = new()
                {
                    AccessionNumber = "0000000000-25-000001",
                    PeriodOfReport = reportDate.ToString("yyyy-MM-dd"),
                    FilingDate = "2025-11-03",
                    FormType = "13F-HR",
                    Cik = "1649339",
                },
            },
            CusipMapping = (mapped ?? []).ToDictionary(c => c, _ => Guid.NewGuid()),
        };

        foreach (var (cusip, issuer, dollars) in tally)
        {
            var entry = new UnmappedCusipTally();
            entry.Add(issuer, dollars);
            context.UnmappedCusips[(cusip, reportDate)] = entry;
        }

        return context;
    }

    private async Task<List<UnmappedCusip>> Rows()
    {
        var ctx = FreshContext();
        return await ctx.Set<UnmappedCusip>().AsNoTracking().ToListAsync();
    }

    private EquiblesFinancialDbContext FreshContext()
    {
        var ctx = _fixture.CreateDbContext();
        _contexts.Add(ctx);
        return ctx;
    }

    private IServiceScopeFactory CreateScopeFactory()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var ctx = FreshContext();
                var sp = Substitute.For<IServiceProvider>();
                sp.GetService(typeof(EquiblesFinancialDbContext)).Returns(ctx);
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }
}
