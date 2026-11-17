using System.Globalization;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

// Filings the sweep imported before it recorded confidential-treatment reports are read once,
// newest day first, down to the day after the newest data set read.
[Collection(ParadeDbCollection.Name)]
public class Form13FConfidentialGapTests(ParadeDbFixture fixture) : IAsyncLifetime
{
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly Realtime13FIngestionService _ingestion =
        Substitute.For<Realtime13FIngestionService>(
            Substitute.For<ISecEdgarClient>(),
            new Filing13FXmlParser(),
            new Realtime13FArchiveBuilder(),
            null,
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<Realtime13FIngestionService>>()
        );

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var context in _contexts)
            context.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Fill_WalksBackToTheNewestReadDataSet_RetriesAnUnreadDay_ThenEnds()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await MarkRead(today.AddDays(-4));
        var unreadable = today.AddDays(-2);
        _ingestion
            .RecordConfidentialFilingsOf(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<DateOnly>() != unreadable);
        var worker = Worker();

        await worker.FillConfidentialFilingGap(CancellationToken.None);

        (await Cursor()).Should().Be(today.AddDays(-1), "the unread day stays ahead of the cursor");

        _ingestion
            .RecordConfidentialFilingsOf(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(true);
        await worker.FillConfidentialFilingGap(CancellationToken.None);
        _ingestion.ClearReceivedCalls();
        await worker.FillConfidentialFilingGap(CancellationToken.None);

        (await Cursor()).Should().Be(today.AddDays(-3), "the data set records its own days");
        await _ingestion
            .DidNotReceive()
            .RecordConfidentialFilingsOf(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fill_BeforeAnyDataSetIsRead_ReadsNothing()
    {
        await Worker().FillConfidentialFilingGap(CancellationToken.None);

        await _ingestion
            .DidNotReceive()
            .RecordConfidentialFilingsOf(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
        (await Cursor()).Should().BeNull();
    }

    private async Task MarkRead(DateOnly through)
    {
        await using var db = fixture.CreateDbContext();
        var end = through.ToString("ddMMMyyyy", CultureInfo.InvariantCulture).ToLowerInvariant();
        db.Add(
            new Form13FNoticeDataSet
            {
                FileName = $"01jan2020-{end}_form13f.zip",
                ParserVersion = Form13FNoticeDataSet.CurrentParserVersion,
            }
        );
        await db.SaveChangesAsync();
    }

    private async Task<DateOnly?> Cursor()
    {
        await using var db = fixture.CreateDbContext();
        return (
            await db.Set<RealtimeSweepState>()
                .SingleOrDefaultAsync(state =>
                    state.WorkerName == HoldingsScraperWorker.ConfidentialGapStateName
                )
        )?.SweptThrough;
    }

    private HoldingsScraperWorker Worker()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory
            .CreateScope()
            .Returns(_ =>
            {
                var context = fixture.CreateDbContext();
                _contexts.Add(context);
                var provider = Substitute.For<IServiceProvider>();
                provider.GetService(typeof(EquiblesFinancialDbContext)).Returns(context);
                provider
                    .GetService(typeof(RealtimeSweepStateRepository))
                    .Returns(new RealtimeSweepStateRepository(context));
                provider.GetService(typeof(Realtime13FIngestionService)).Returns(_ingestion);
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(provider);
                return scope;
            });
        return new HoldingsScraperWorker(
            Substitute.For<ILogger<HoldingsScraperWorker>>(),
            scopeFactory,
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Options.Create(new WorkerOptions()),
            new ConfigurationBuilder().Build(),
            new HoldingsRescanSignal()
        );
    }
}
