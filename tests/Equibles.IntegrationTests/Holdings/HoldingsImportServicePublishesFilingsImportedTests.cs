using System.IO.Compression;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.IntegrationTests.Helpers;
using Equibles.Messaging.Contracts.Holdings;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// Contract: after a successful 13F import, <see cref="HoldingsImportService"/>
/// publishes one <see cref="Filings13FImported"/> per distinct <c>ReportDate</c>
/// that received filings — deduplicating the per-filing volume so the consumer
/// rebuilds one snapshot per quarter, not one per filing.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class HoldingsImportServicePublishesFilingsImportedTests : IAsyncLifetime
{
    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];

    public HoldingsImportServicePublishesFilingsImportedTests(ParadeDbFixture fixture) =>
        _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        return Task.CompletedTask;
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
                sp.GetService(typeof(CommonStockRepository))
                    .Returns(new CommonStockRepository(ctx));
                sp.GetService(typeof(InstitutionalHolderRepository))
                    .Returns(new InstitutionalHolderRepository(ctx));
                sp.GetService(typeof(InstitutionalHoldingRepository))
                    .Returns(new InstitutionalHoldingRepository(ctx));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    private static ZipArchive BuildArchive(params (string Name, string Body)[] entries)
    {
        var buffer = new MemoryStream();
        using (var writer = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, body) in entries)
            {
                var entry = writer.CreateEntry(name);
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(body);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private static IStockPriceProvider PriceProviderReturning(
        Dictionary<(Guid, DateOnly), decimal> prices
    )
    {
        var provider = Substitute.For<IStockPriceProvider>();
        provider
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(prices));
        return provider;
    }

    [Fact]
    public async Task ImportDataSet_TwoFilingsInSameQuarter_PublishesOneEventForThatQuarter()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
            Cusip = "037833100",
        };
        using (var seed = FreshContext())
        {
            seed.Set<CommonStock>().Add(stock);
            await seed.SaveChangesAsync();
        }

        var reportDate = new DateOnly(2024, 9, 30);
        var submission =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "13F-HR\tACC-A\t2024-10-15\t2024-09-30\t0001000001\n"
            + "13F-HR\tACC-B\t2024-10-16\t2024-09-30\t0001000002\n";
        var coverPage =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\tFILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\n"
            + "ACC-A\tN\tFilerA\tNY\tNY\t028-1\t1\n"
            + "ACC-B\tN\tFilerB\tNY\tNY\t028-2\t2\n";
        var infoTable =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\tVOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n"
            + "ACC-A\t037833100\t100\tSH\t\tSOLE\t100\t0\t0\tCOM\t\n"
            + "ACC-B\t037833100\t200\tSH\t\tSOLE\t200\t0\t0\tCOM\t\n";

        using var archive = BuildArchive(
            ("SUBMISSION.tsv", submission),
            ("COVERPAGE.tsv", coverPage),
            ("INFOTABLE.tsv", infoTable)
        );

        var bus = Substitute.For<IBus>();
        var sut = new HoldingsImportService(
            CreateScopeFactory(),
            NullLogger<HoldingsImportService>.Instance,
            Options.Create(new WorkerOptions()),
            PriceProviderReturning(
                new Dictionary<(Guid, DateOnly), decimal> { [(stock.Id, reportDate)] = 150m }
            ),
            bus
        );

        var result = await sut.ImportDataSet(
            archive,
            new DateOnly(2024, 1, 1),
            CancellationToken.None
        );
        result.IsComplete.Should().BeTrue();

        await bus.Received(1)
            .Publish(
                Arg.Is<Filings13FImported>(e => e.ReportDate == reportDate && e.FilingCount == 2),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ImportDataSet_FilingsAcrossTwoQuarters_PublishesOneEventPerQuarter()
    {
        var stock = new CommonStock
        {
            Id = Guid.NewGuid(),
            Ticker = "AAPL",
            Name = "Apple Inc",
            Cik = "0000320193",
            Cusip = "037833100",
        };
        using (var seed = FreshContext())
        {
            seed.Set<CommonStock>().Add(stock);
            await seed.SaveChangesAsync();
        }

        var q3 = new DateOnly(2024, 9, 30);
        var q4 = new DateOnly(2024, 12, 31);
        var submission =
            "SUBMISSIONTYPE\tACCESSION_NUMBER\tFILING_DATE\tPERIODOFREPORT\tCIK\n"
            + "13F-HR\tACC-Q3\t2024-10-15\t2024-09-30\t0001000003\n"
            + "13F-HR\tACC-Q4\t2025-02-10\t2024-12-31\t0001000004\n";
        var coverPage =
            "ACCESSION_NUMBER\tISAMENDMENT\tFILINGMANAGER_NAME\tFILINGMANAGER_CITY\tFILINGMANAGER_STATEORCOUNTRY\tFORM13FFILENUMBER\tCRDNUMBER\n"
            + "ACC-Q3\tN\tFilerA\tNY\tNY\t028-3\t3\n"
            + "ACC-Q4\tN\tFilerB\tNY\tNY\t028-4\t4\n";
        var infoTable =
            "ACCESSION_NUMBER\tCUSIP\tSSHPRNAMT\tSSHPRNAMTTYPE\tPUTCALL\tINVESTMENTDISCRETION\tVOTING_AUTH_SOLE\tVOTING_AUTH_SHARED\tVOTING_AUTH_NONE\tTITLEOFCLASS\tOTHERMANAGER\n"
            + "ACC-Q3\t037833100\t100\tSH\t\tSOLE\t100\t0\t0\tCOM\t\n"
            + "ACC-Q4\t037833100\t200\tSH\t\tSOLE\t200\t0\t0\tCOM\t\n";

        using var archive = BuildArchive(
            ("SUBMISSION.tsv", submission),
            ("COVERPAGE.tsv", coverPage),
            ("INFOTABLE.tsv", infoTable)
        );

        var bus = Substitute.For<IBus>();
        var sut = new HoldingsImportService(
            CreateScopeFactory(),
            NullLogger<HoldingsImportService>.Instance,
            Options.Create(new WorkerOptions()),
            PriceProviderReturning(
                new Dictionary<(Guid, DateOnly), decimal>
                {
                    [(stock.Id, q3)] = 150m,
                    [(stock.Id, q4)] = 160m,
                }
            ),
            bus
        );

        var result = await sut.ImportDataSet(
            archive,
            new DateOnly(2024, 1, 1),
            CancellationToken.None
        );
        result.IsComplete.Should().BeTrue();

        await bus.Received(1)
            .Publish(
                Arg.Is<Filings13FImported>(e => e.ReportDate == q3),
                Arg.Any<CancellationToken>()
            );
        await bus.Received(1)
            .Publish(
                Arg.Is<Filings13FImported>(e => e.ReportDate == q4),
                Arg.Any<CancellationToken>()
            );
    }
}
