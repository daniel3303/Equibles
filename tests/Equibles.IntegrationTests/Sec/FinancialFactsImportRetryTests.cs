using System.Data.Common;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Configuration;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Equibles.Sec.FinancialFacts.Repositories;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

[Collection(ParadeDbCollection.Name)]
public class FinancialFactsImportRetryTests(ParadeDbFixture fixture) : IAsyncLifetime
{
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly FaultAfterFirstBatch _fault = new();
    private readonly FinancialFactBatchInterceptor _writes = new();
    private readonly Clock _clock = new();

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var context in _contexts)
            context.Dispose();
        return Task.CompletedTask;
    }

    private IServiceScopeFactory Scopes()
    {
        var factory = Substitute.For<IServiceScopeFactory>();
        factory
            .CreateScope()
            .Returns(_ =>
            {
                var db = fixture.CreateDbContext(options =>
                    options.AddInterceptors(_fault, _writes)
                );
                _contexts.Add(db);
                var provider = Substitute.For<IServiceProvider>();
                provider.GetService(typeof(EquiblesFinancialDbContext)).Returns(db);
                provider
                    .GetService(typeof(ISharesOutstandingProvider))
                    .Returns(Substitute.For<ISharesOutstandingProvider>());
                provider
                    .GetService(typeof(FinancialConceptRepository))
                    .Returns(new FinancialConceptRepository(db));
                provider
                    .GetService(typeof(FinancialFactsSyncStatusRepository))
                    .Returns(new FinancialFactsSyncStatusRepository(db));
                provider.GetService(typeof(DocumentRepository)).Returns(new DocumentRepository(db));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(provider);
                return scope;
            });
        return factory;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialCommit_RestartHonorsCooldown_ThenSkipsUnchangedPrefixAndCompletes(
        bool cancelled
    )
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "AAPL",
            Name: "Apple",
            Cik: "0000320193"
        );
        await using (var seed = fixture.CreateDbContext())
        {
            seed.Add(issuer);
            await seed.SaveChangesAsync();
        }
        var client = Substitute.For<ISecEdgarClient>();
        var values = Enumerable
            .Range(2021, 3)
            .Select(year => new CompanyFactValue
            {
                Start = new DateOnly(year, 1, 1),
                End = new DateOnly(year, 12, 31),
                Val = year,
                Accn = $"0000320193-{year % 100:00}-000001",
                Fy = year,
                Fp = "FY",
                Form = "10-K",
                Filed = new DateOnly(year + 1, 2, 1),
            })
            .ToList();
        client
            .GetCompanyFacts(issuer.Cik)
            .Returns(
                new CompanyFactsResponse
                {
                    Cik = 320193,
                    Facts = new()
                    {
                        ["us-gaap"] = new()
                        {
                            ["Revenues"] = new CompanyFactConcept
                            {
                                Label = "Revenues",
                                Units = new() { ["USD"] = values },
                            },
                        },
                    },
                }
            );
        FinancialFactsImportService Importer() =>
            new(
                Scopes(),
                client,
                Substitute.For<ILogger<FinancialFactsImportService>>(),
                Substitute.For<ErrorReporter>(
                    Substitute.For<IServiceScopeFactory>(),
                    Substitute.For<ILogger<ErrorReporter>>()
                ),
                persistenceOptions: Options.Create(
                    new FinancialFactsPersistenceOptions { InsertBatchSize = 1 }
                ),
                timeProvider: _clock
            );

        using var cancellation = new CancellationTokenSource();
        if (cancelled)
        {
            _fault.Cancellation = cancellation;
            var import = () => Importer().Import(issuer, cancellation.Token);
            await import.Should().ThrowAsync<OperationCanceledException>();
        }
        else
            await Importer().Import(issuer, CancellationToken.None);
        await using (var verify = fixture.CreateDbContext())
        {
            (await verify.Set<FinancialFact>().CountAsync()).Should().Be(1);
            var status = await verify.Set<FinancialFactsSyncStatus>().SingleAsync();
            status.LastFiledDateSeen.Should().BeNull();
            status.LastCheckedAt.Should().Be(default(DateTime));
            status.ImporterVersion.Should().Be(0);
            status.ImportAttempts.Should().Be(1);
            status.NextAttemptAt.Should().Be(_clock.Now.AddHours(1).UtcDateTime);
        }
        _fault.Enabled = false;
        await Importer().Import(issuer, CancellationToken.None);
        await client.Received(1).GetCompanyFacts(issuer.Cik);
        _writes.FactBatchSizes.Should().Equal(1);

        _clock.Now = _clock.Now.AddHours(1);
        await Importer().Import(issuer, CancellationToken.None);
        _writes.FactBatchSizes.Should().Equal(1, 0, 1, 1);
        await using (var verify = fixture.CreateDbContext())
        {
            (await verify.Set<FinancialFact>().CountAsync()).Should().Be(3);
            var status = await verify.Set<FinancialFactsSyncStatus>().SingleAsync();
            status.LastFiledDateSeen.Should().Be(new DateOnly(2024, 2, 1));
            status.ImportAttempts.Should().Be(0);
            status.NextAttemptAt.Should().BeNull();
            status.ImportAttemptId.Should().BeNull();
        }

        // Same natural keys: nullable metadata changes must still reach PostgreSQL.
        values[0].Frame = "CY2021";
        await ForceReplay(issuer.Id);
        await Importer().Import(issuer, CancellationToken.None);
        await using (var verify = fixture.CreateDbContext())
            (await verify.Set<FinancialFact>().OrderBy(row => row.PeriodEnd).FirstAsync())
                .Frame.Should()
                .Be("CY2021");
        values[0].Frame = null;
        await ForceReplay(issuer.Id);
        await Importer().Import(issuer, CancellationToken.None);
        await using (var verify = fixture.CreateDbContext())
            (await verify.Set<FinancialFact>().OrderBy(row => row.PeriodEnd).FirstAsync())
                .Frame.Should()
                .BeNull();
        _writes.FactBatchSizes.Skip(4).Should().Equal(1, 0, 0, 1, 0, 0);
    }

    private async Task ForceReplay(Guid issuerId)
    {
        await using var db = fixture.CreateDbContext();
        await db.Set<FinancialFactsSyncStatus>()
            .Where(row => row.EquityIssuerId == issuerId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ImporterVersion, 0));
    }

    [Fact]
    public async Task InterruptedAttempts_BackOffToADay_AndStaleOwnerCannotAcknowledgeNewAttempt()
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "AAPL",
            Name: "Apple",
            Cik: "0000320193"
        );
        await using (var db = fixture.CreateDbContext())
        {
            db.Add(issuer);
            await db.SaveChangesAsync();
        }
        var attempts = new FinancialFactsImportAttempts(Scopes(), _clock);
        var first = await attempts.TryBegin(issuer.Id, CancellationToken.None);
        first.Should().NotBeNull();
        (await attempts.TryBegin(issuer.Id, CancellationToken.None)).Should().BeNull();
        Guid? current = first;
        foreach (var hours in new[] { 1, 2, 4, 8, 16, 24 })
        {
            _clock.Now = _clock.Now.AddHours(hours);
            current = await attempts.TryBegin(issuer.Id, CancellationToken.None);
            current.Should().NotBeNull();
        }
        await attempts.Complete(
            issuer.Id,
            first.Value,
            new DateOnly(2024, 1, 1),
            "old",
            CancellationToken.None
        );
        await using (var db = fixture.CreateDbContext())
        {
            var status = await db.Set<FinancialFactsSyncStatus>().SingleAsync();
            status.LastFiledDateSeen.Should().BeNull();
            status.ImportAttemptId.Should().Be(current);
            status.NextAttemptAt.Should().Be(_clock.Now.AddHours(24).UtcDateTime);
        }
        await attempts.Complete(
            issuer.Id,
            current.Value,
            new DateOnly(2025, 1, 1),
            "current",
            CancellationToken.None
        );
        await using (var db = fixture.CreateDbContext())
            (await db.Set<FinancialFactsSyncStatus>().SingleAsync())
                .LastFiledDateSeen.Should()
                .Be(new DateOnly(2025, 1, 1));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FaultAfterFirstBatch : DbCommandInterceptor
    {
        public bool Enabled { get; set; } = true;
        public CancellationTokenSource Cancellation { get; set; }
        private int _batches;

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                Enabled
                && command.CommandText.StartsWith("INSERT INTO")
                && command.CommandText.Contains("\"FinancialFact\"")
                && ++_batches == 2
            )
            {
                if (Cancellation != null)
                {
                    Cancellation.Cancel();
                    throw new OperationCanceledException(Cancellation.Token);
                }
                throw new IOException("Injected failure after a committed batch");
            }
            return ValueTask.FromResult(result);
        }
    }
}
