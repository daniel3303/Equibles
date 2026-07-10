using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Media.BusinessLogic;
using Equibles.Media.Data;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Equibles.Yahoo.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Pins which insider-processor skip outcomes tombstone the filing and which
/// never may. Deterministic content verdicts (legacy non-XML, malformed XML,
/// stale superseded amendment) previously persisted nothing, so every
/// enumeration re-downloaded the multi-MB submission just to re-skip it — the
/// processor-path poison shape. Company-relative or transient outcomes
/// (issuer mismatch, empty content) must NEVER tombstone: the tombstone is
/// keyed by accession and consulted globally, so recording a mismatch would
/// suppress the real issuer's ingest of the same filing.
/// </summary>
public class InsiderTradingFilingProcessorSkipTombstoneTests
{
    private const string Accession = "0001185185-23-001302";
    private const string IssuerCik = "1236275";
    private const string OwnerCik = "0009999999";

    private sealed class SecTombstoneModuleConfiguration : IModuleConfiguration
    {
        public void ConfigureEntities(ModelBuilder builder)
        {
            builder.Entity<FailedFilingIngest>();
            builder.Entity<InsiderOwner>();
            builder.Entity<InsiderTransaction>();
            builder.Entity<InsiderFiling>();
        }
    }

    private static EquiblesFinancialDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .EnableServiceProviderCaching(false)
                .Options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new MediaModuleConfiguration(),
                new SecTombstoneModuleConfiguration(),
            }
        );

    private static InsiderTradingFilingProcessor BuildProcessor(
        EquiblesFinancialDbContext ctx,
        string documentContent
    )
    {
        var secEdgar = Substitute.For<ISecEdgarClient>();
        secEdgar.GetDocumentContent(Arg.Any<FilingData>()).Returns(documentContent);

        var services = new ServiceCollection();
        services.AddSingleton(ctx);
        services.AddSingleton(secEdgar);
        services.AddSingleton(Substitute.For<IFileManager>());
        services.AddScoped<InsiderOwnerRepository>();
        services.AddScoped<InsiderTransactionRepository>();
        services.AddScoped<InsiderFilingRepository>();
        services.AddScoped<FailedFilingIngestRepository>();
        services.AddScoped<DailyStockPriceRepository>();
        services.AddScoped<InsiderTransactionPriceValidator>();
        var scopeFactory = services
            .BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        return new InsiderTradingFilingProcessor(
            scopeFactory,
            Substitute.For<ILogger<InsiderTradingFilingProcessor>>(),
            new Equibles.Errors.BusinessLogic.ErrorReporter(
                scopeFactory,
                Substitute.For<ILogger<Equibles.Errors.BusinessLogic.ErrorReporter>>()
            )
        );
    }

    private static CommonStock Issuer() =>
        new()
        {
            Id = Guid.NewGuid(),
            Ticker = "QXO",
            Cik = IssuerCik,
        };

    private static FilingData Filing(string form = "4") =>
        new()
        {
            Cik = IssuerCik,
            AccessionNumber = Accession,
            FilingDate = new DateOnly(2023, 6, 1),
            ReportDate = new DateOnly(2023, 5, 30),
            Form = form,
        };

    private static string OwnershipXml(
        string issuerCik = IssuerCik,
        string documentType = "4",
        string dateOfOriginalSubmission = null,
        bool includeOwner = true
    )
    {
        var original =
            dateOfOriginalSubmission == null
                ? ""
                : $"<dateOfOriginalSubmission>{dateOfOriginalSubmission}</dateOfOriginalSubmission>";
        var owner = includeOwner
            ? $"""
                <reportingOwner>
                    <reportingOwnerId>
                        <rptOwnerCik>{OwnerCik}</rptOwnerCik>
                        <rptOwnerName>Doe John</rptOwnerName>
                    </reportingOwnerId>
                </reportingOwner>
                """
            : "<reportingOwner></reportingOwner>";
        return $"""
            <ownershipDocument>
                <schemaVersion>X0306</schemaVersion>
                <documentType>{documentType}</documentType>
                {original}
                <periodOfReport>2023-05-30</periodOfReport>
                <issuer>
                    <issuerCik>{issuerCik}</issuerCik>
                    <issuerName>QXO, Inc.</issuerName>
                    <issuerTradingSymbol>QXO</issuerTradingSymbol>
                </issuer>
                {owner}
            </ownershipDocument>
            """;
    }

    [Fact]
    public async Task LegacyNonXmlFiling_IsTombstoned()
    {
        await using var ctx = CreateContext();
        var processor = BuildProcessor(ctx, "PEM plain-text paper filing, no XML here");

        var processed = await processor.Process(Filing(), Issuer());

        processed.Should().BeFalse();
        var tombstone = await ctx.Set<FailedFilingIngest>().SingleAsync();
        tombstone.AccessionNumber.Should().Be(Accession);
        tombstone.LastError.Should().Contain("legacy non-XML");
    }

    [Fact]
    public async Task MalformedOwnershipXml_IsTombstoned()
    {
        await using var ctx = CreateContext();
        var processor = BuildProcessor(ctx, "<ownershipDocument><unclosed></ownershipDocument");

        var processed = await processor.Process(Filing(), Issuer());

        processed.Should().BeFalse();
        var tombstone = await ctx.Set<FailedFilingIngest>().SingleAsync();
        tombstone.LastError.Should().Contain("malformed ownership XML");
    }

    [Fact]
    public async Task StaleAmendment_NewerAmendmentAlreadyIngested_IsTombstoned()
    {
        await using var ctx = CreateContext();
        var issuer = Issuer();
        var owner = new InsiderOwner { OwnerCik = OwnerCik, Name = "Doe John" };
        ctx.Set<InsiderOwner>().Add(owner);
        await ctx.SaveChangesAsync();
        // A NEWER amendment of the same original is already ingested.
        ctx.Set<InsiderTransaction>()
            .Add(
                new InsiderTransaction
                {
                    InsiderOwnerId = owner.Id,
                    CommonStockId = issuer.Id,
                    FilingDate = new DateOnly(2023, 7, 1),
                    TransactionDate = new DateOnly(2023, 5, 30),
                    TransactionCode = TransactionCode.Other,
                    AccessionNumber = "0001185185-23-009999",
                    SecurityTitle = "Common Stock",
                    TransactionOrder = 1,
                    IsAmendment = true,
                    OriginalFilingDate = new DateOnly(2023, 5, 25),
                    IsPriceValid = true,
                    Notes = [],
                    ParserVersion = InsiderTransaction.CurrentParserVersion,
                }
            );
        await ctx.SaveChangesAsync();

        var processor = BuildProcessor(
            ctx,
            OwnershipXml(documentType: "4/A", dateOfOriginalSubmission: "2023-05-25")
        );

        var processed = await processor.Process(Filing(form: "4/A"), issuer);

        processed.Should().BeFalse();
        var tombstone = await ctx.Set<FailedFilingIngest>().SingleAsync();
        tombstone.LastError.Should().Contain("newer ingested amendment");
    }

    [Fact]
    public async Task IssuerMismatch_IsNeverTombstoned()
    {
        await using var ctx = CreateContext();
        // The filing surfaced via a reporting-owner feed: its issuer is another
        // company. Tombstoning it would suppress the real issuer's ingest.
        var processor = BuildProcessor(ctx, OwnershipXml(issuerCik: "7777777"));

        var processed = await processor.Process(Filing(), Issuer());

        processed.Should().BeFalse();
        (await ctx.Set<FailedFilingIngest>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task EmptyContent_IsNeverTombstoned()
    {
        await using var ctx = CreateContext();
        var processor = BuildProcessor(ctx, "");

        var processed = await processor.Process(Filing(), Issuer());

        processed.Should().BeFalse();
        (await ctx.Set<FailedFilingIngest>().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task MissingReportingOwnerIdentity_IsTombstoned()
    {
        await using var ctx = CreateContext();
        var processor = BuildProcessor(ctx, OwnershipXml(includeOwner: false));

        var processed = await processor.Process(Filing(), Issuer());

        processed.Should().BeFalse();
        var tombstone = await ctx.Set<FailedFilingIngest>().SingleAsync();
        tombstone.LastError.Should().Contain("reporting-owner identity");
    }
}
