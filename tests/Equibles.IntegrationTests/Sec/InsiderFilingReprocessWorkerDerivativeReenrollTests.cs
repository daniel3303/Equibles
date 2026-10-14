using System.Reflection;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CorporateActions.Repositories;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.IntegrationTests.InsiderTrading;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.HostedService;
using Equibles.Sec.Repositories;
using Equibles.Yahoo.Data.Models;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using File = Equibles.Media.Data.Models.File;
using FileContent = Equibles.Media.Data.Models.FileContent;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Pins the worker side of the derivative-total re-enrollment: the one-shot pass runs before the
/// reprocess so the CoreWeave call sealed valid by the old rule is repaired in the same cycle, and
/// its marker keeps the non-self-terminating predicate from re-enrolling a legit high unit price
/// (a convertible note counted by principal) on every later cycle.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class InsiderFilingReprocessWorkerDerivativeReenrollTests : ParadeDbMcpTestBase
{
    private const string Marker = "InsiderTrading.DerivativeTotalsReenroll";
    private const string CallAccession = "0001104659-26-097430";
    private const string NoteAccession = "0001104659-26-097431";

    public InsiderFilingReprocessWorkerDerivativeReenrollTests(ParadeDbFixture fixture)
        : base(fixture) { }

    private static string DerivativeXml(
        string title,
        string exercisePrice,
        string shares,
        string price,
        string underlyingShares
    ) =>
        "<ownershipDocument>"
        + "<documentType>4</documentType>"
        + "<periodOfReport>2026-08-18</periodOfReport>"
        + "<derivativeTable><derivativeTransaction>"
        + $"<securityTitle><value>{title}</value></securityTitle>"
        + $"<conversionOrExercisePrice><value>{exercisePrice}</value></conversionOrExercisePrice>"
        + "<transactionDate><value>2026-08-18</value></transactionDate>"
        + "<transactionCoding><transactionCode>S</transactionCode></transactionCoding>"
        + "<transactionAmounts>"
        + $"<transactionShares><value>{shares}</value></transactionShares>"
        + $"<transactionPricePerShare><value>{price}</value></transactionPricePerShare>"
        + "<transactionAcquiredDisposedCode><value>D</value></transactionAcquiredDisposedCode>"
        + "</transactionAmounts>"
        + "<underlyingSecurity>"
        + "<underlyingSecurityTitle><value>Class A Common Stock</value></underlyingSecurityTitle>"
        + $"<underlyingSecurityShares><value>{underlyingShares}</value></underlyingSecurityShares>"
        + "</underlyingSecurity>"
        + "</derivativeTransaction></derivativeTable>"
        + "</ownershipDocument>";

    private static InsiderFiling CachedFiling(string accession, string xml)
    {
        var rawBytes = Encoding.UTF8.GetBytes(xml);
        return new InsiderFiling
        {
            AccessionNumber = accession,
            CaptureStatus = InsiderFilingCaptureStatus.Captured,
            UncompressedSize = rawBytes.Length,
            Content = new File
            {
                Name = accession,
                Extension = "gz",
                ContentType = "application/gzip",
                FileContent = new FileContent { Bytes = GzipCompressor.Compress(rawBytes) },
            },
        };
    }

    private ServiceProvider BuildServices(ISecEdgarClient edgar)
    {
        var services = new ServiceCollection();
        services.AddScoped<EquiblesFinancialDbContext>(_ => Fixture.CreateDbContext());
        services.AddScoped<InsiderTransactionRepository>();
        services.AddScoped<InsiderFilingRepository>();
        services.AddScoped<EquityDailyStockPriceRepository>();
        services.AddScoped<StockSplitRepository>();
        services.AddScoped<BackfillStateRepository>();
        services.AddScoped<InsiderMisrepairedPriceSweep>();
        services.AddScoped<InsiderFilingReprocessManager>();
        services.AddScoped<InsiderTransactionPriceBackfillManager>();
        services.AddSingleton<InsiderTransactionPriceValidator>();
        services.AddSingleton(edgar);
        services.AddSingleton(InsiderReprocessTestSupport.NewFileManager());
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        return services.BuildServiceProvider();
    }

    private static Task RunCycle(InsiderFilingReprocessWorker worker)
    {
        var doWork = typeof(InsiderFilingReprocessWorker).GetMethod(
            "DoWork",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        return (Task)doWork!.Invoke(worker, [CancellationToken.None]);
    }

    [Fact]
    public async Task DoWork_DerivativeTotalSealedValid_RepairsInTheSameCycleAndRunsOnlyOnce()
    {
        var date = new DateOnly(2026, 8, 18);
        EquityIssuer stock = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "CRWV",
            Name: "CoreWeave, Inc.",
            Cik: "0001769628"
        );
        var owner = new InsiderOwner
        {
            Id = Guid.NewGuid(),
            OwnerCik = "0001352851",
            Name = "Fund Owner",
            City = "Evanston",
            StateOrCountry = "IL",
        };

        // Both rows carry the old rule's verdict: valid as filed at the current parser version.
        InsiderTransaction Sealed(string accession, string title, long shares, decimal price) =>
            new()
            {
                Id = Guid.NewGuid(),
                EquityIssuerId = stock.Id,
                InsiderOwnerId = owner.Id,
                AccessionNumber = accession,
                TransactionOrder = 0,
                FilingDate = new DateOnly(2026, 8, 20),
                TransactionDate = date,
                TransactionCode = TransactionCode.Sale,
                Shares = shares,
                PricePerShare = price,
                ReportedPricePerShare = price,
                IsPriceValid = true,
                AcquiredDisposed = AcquiredDisposed.Disposed,
                OwnershipNature = OwnershipNature.Direct,
                SecurityTitle = title,
                SecurityKind = InsiderSecurityKind.Derivative,
                FilingForm = InsiderOwnershipForm.Form4,
                ParserVersion = InsiderTransaction.CurrentParserVersion,
            };

        var call = Sealed(CallAccession, "Call option (obligation to sell)", 183_640, 3_659_027m);
        var note = Sealed(NoteAccession, "Convertible Note", 5_000_000, 5_000_000m);

        DbContext.Add(stock);
        DbContext.Add(owner);
        DbContext.Add(
            new EquityDailyStockPrice
            {
                Listing = Equibles.TestSupport.NativeListingSeed.ForStock(DbContext, stock, null),
                Date = date,
                Close = 108m,
                Low = 104m,
                High = 112m,
                Volume = 1_000,
            }
        );
        DbContext.Add(call);
        DbContext.Add(note);
        DbContext.Add(
            CachedFiling(
                CallAccession,
                DerivativeXml(
                    "Call option (obligation to sell)",
                    "130",
                    "183640",
                    "3659027",
                    "183640"
                )
            )
        );
        DbContext.Add(
            CachedFiling(
                NoteAccession,
                DerivativeXml("Convertible Note", "2", "5000000", "5000000", "2500000")
            )
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        var edgar = Substitute.For<ISecEdgarClient>();
        await using var provider = BuildServices(edgar);
        var worker = new InsiderFilingReprocessWorker(
            Substitute.For<ILogger<InsiderFilingReprocessWorker>>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

        await RunCycle(worker);

        await using (var verify = Fixture.CreateDbContext())
        {
            var repaired = await verify.Set<InsiderTransaction>().SingleAsync(t => t.Id == call.Id);
            repaired.PricePerShare.Should().Be(3_659_027m / 183_640m);
            repaired.PriceWasRepaired.Should().BeTrue();
            repaired.IsPriceValid.Should().BeTrue();
            repaired.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);

            var asFiled = await verify.Set<InsiderTransaction>().SingleAsync(t => t.Id == note.Id);
            asFiled.PricePerShare.Should().Be(5_000_000m);
            asFiled.PriceWasRepaired.Should().BeFalse();
            asFiled.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);

            (await new BackfillStateRepository(verify).GetByName(Marker)).Should().NotBeNull();

            // Drop the note's cache: a second re-enrollment would now need an EDGAR fetch and
            // record a capture attempt, so silence on both proves the marker held.
            var noteFiling = await verify
                .Set<InsiderFiling>()
                .SingleAsync(f => f.AccessionNumber == NoteAccession);
            noteFiling.CaptureStatus = InsiderFilingCaptureStatus.NotChecked;
            await verify.SaveChangesAsync();
        }

        await RunCycle(worker);

        await using var final = Fixture.CreateDbContext();
        var noteAfter = await final.Set<InsiderTransaction>().SingleAsync(t => t.Id == note.Id);
        noteAfter.ParserVersion.Should().Be(InsiderTransaction.CurrentParserVersion);
        (await final.Set<InsiderFiling>().SingleAsync(f => f.AccessionNumber == NoteAccession))
            .CaptureAttempts.Should()
            .Be(0);
        edgar.ReceivedCalls().Should().BeEmpty();
    }
}
