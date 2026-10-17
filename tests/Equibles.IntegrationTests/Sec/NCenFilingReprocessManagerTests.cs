using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Errors.BusinessLogic;
using Equibles.Errors.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Equibles.IntegrationTests.Sec;

[Collection(ParadeDbCollection.Name)]
public class NCenFilingReprocessManagerTests : ParadeDbMcpTestBase
{
    private const string WindsorAccession = "0000107606-26-000003";
    private const string WindsorCik = "0000107606";

    private readonly ISecEdgarClient _secClient = Substitute.For<ISecEdgarClient>();

    public NCenFilingReprocessManagerTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task Run_StaleFiling_RereadsItAndReplacesItsProviders()
    {
        var filingId = await SeedStaleWindsor();
        Returns(WindsorAccession, NCenSubmissionFixtures.VanguardWindsor);

        var result = await Manager().Run();

        result.Total.Should().Be(1);
        result.Processed.Should().Be(1);
        result.Failed.Should().Be(0);
        await using var verify = Fixture.CreateDbContext();
        var filing = await verify.Set<NCenFiling>().SingleAsync(f => f.Id == filingId);
        filing.IsFamilyInvestmentCompany.Should().BeTrue();
        filing.ParserVersion.Should().Be(NCenFiling.CurrentParserVersion);
        filing.ReprocessAttempts.Should().Be(0);
        filing.RegistrantName.Should().Be("VANGUARD WINDSOR FUNDS");
        filing.IsAmendment.Should().BeFalse();
        var providers = await verify
            .Set<NCenServiceProvider>()
            .Where(p => p.NCenFilingId == filingId)
            .ToListAsync();
        providers.Should().HaveCount(10);
        providers.Should().NotContain(p => p.Name == "Stale Adviser LLC");
        providers
            .Should()
            .ContainSingle(p => p.ProviderType == NCenServiceProviderType.InvestmentAdviser)
            .Which.Country.Should()
            .Be("US");
    }

    [Fact]
    public async Task Run_CurrentFiling_IsNotFetched()
    {
        await SeedStaleWindsor(parserVersion: NCenFiling.CurrentParserVersion);

        var result = await Manager().Run();

        result.Total.Should().Be(0);
        await _secClient
            .DidNotReceiveWithAnyArgs()
            .GetDocumentContent(default(string), default, default);
    }

    [Fact]
    public async Task Run_EdgarOutage_StopsWithoutBurningAnAttempt()
    {
        var filingId = await SeedStaleWindsor();
        _secClient
            .GetDocumentContent(WindsorAccession, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(
                new HttpRequestException(
                    "Service Unavailable",
                    null,
                    HttpStatusCode.ServiceUnavailable
                )
            );

        var result = await Manager().Run();

        result.StoppedByOutage.Should().BeTrue();
        result.Failed.Should().Be(0);
        await using var verify = Fixture.CreateDbContext();
        var filing = await verify.Set<NCenFiling>().SingleAsync(f => f.Id == filingId);
        filing.ParserVersion.Should().Be(0);
        filing.ReprocessAttempts.Should().Be(0);
    }

    [Fact]
    public async Task Run_PulledSubmission_KeepsItsStoredFactsAtTheAttemptCeiling()
    {
        var filingId = await SeedStaleWindsor();
        _secClient
            .GetDocumentContent(WindsorAccession, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Not Found", null, HttpStatusCode.NotFound));

        for (var pass = 1; pass <= NCenFilingReprocessManager.MaxReprocessAttempts; pass++)
        {
            var result = await Manager().Run();
            result.Failed.Should().Be(1);

            await using var verify = Fixture.CreateDbContext();
            var filing = await verify.Set<NCenFiling>().SingleAsync(f => f.Id == filingId);
            filing.ReprocessAttempts.Should().Be(pass);
            filing
                .ParserVersion.Should()
                .Be(
                    pass == NCenFilingReprocessManager.MaxReprocessAttempts
                        ? NCenFiling.CurrentParserVersion
                        : 0
                );
            filing.IsFamilyInvestmentCompany.Should().BeFalse();
        }

        (await Manager().Run()).Total.Should().Be(0);
        await using var final = Fixture.CreateDbContext();
        (await final.Set<NCenServiceProvider>().CountAsync(p => p.NCenFilingId == filingId))
            .Should()
            .Be(1, "a filing that cannot be re-read keeps its stored providers");
    }

    [Fact]
    public async Task Run_UnreadableSubmission_BurnsAnAttemptAndMovesOn()
    {
        var unreadable = await SeedStaleWindsor();
        var readable = await SeedStale(
            "0001193125-26-421574",
            "0001540305",
            "ESS",
            new DateOnly(2026, 10, 1)
        );
        Returns(WindsorAccession, "<SEC-DOCUMENT>no xml here</SEC-DOCUMENT>");
        Returns("0001193125-26-421574", NCenSubmissionFixtures.EtfSeriesSolutions);

        var result = await Manager().Run();

        result.Processed.Should().Be(1);
        result.Failed.Should().Be(1);
        await using var verify = Fixture.CreateDbContext();
        (await verify.Set<NCenFiling>().SingleAsync(f => f.Id == unreadable))
            .ReprocessAttempts.Should()
            .Be(1);
        (await verify.Set<NCenFiling>().SingleAsync(f => f.Id == readable))
            .ParserVersion.Should()
            .Be(NCenFiling.CurrentParserVersion);
    }

    private void Returns(string accession, string content) =>
        _secClient
            .GetDocumentContent(accession, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(content);

    private Task<Guid> SeedStaleWindsor(int parserVersion = 0) =>
        SeedStale(WindsorAccession, WindsorCik, "VWNDX", new DateOnly(2026, 1, 9), parserVersion);

    // A filing as the old parser stored it: no family flag, one adviser without a country.
    private async Task<Guid> SeedStale(
        string accession,
        string cik,
        string ticker,
        DateOnly filingDate,
        int parserVersion = 0
    )
    {
        EquityIssuer issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: ticker,
            Name: ticker + " registrant",
            Cik: cik
        );
        var filing = new NCenFiling
        {
            EquityIssuerId = issuer.Id,
            AccessionNumber = accession,
            FilingDate = filingDate,
            RegistrantName = "stale name",
            ReportEndingPeriod = new DateOnly(2025, 10, 31),
            ParserVersion = parserVersion,
        };
        DbContext.Add(issuer);
        DbContext.Add(filing);
        DbContext.Add(
            new NCenServiceProvider
            {
                NCenFilingId = filing.Id,
                ProviderType = NCenServiceProviderType.InvestmentAdviser,
                Name = "Stale Adviser LLC",
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        return filing.Id;
    }

    private NCenFilingReprocessManager Manager()
    {
        var context = Fixture.CreateDbContext();
        var errorReporter = new ErrorReporter(
            ServiceScopeSubstitute.Create(
                (typeof(ErrorManager), new ErrorManager(new ErrorRepository(context)))
            ),
            NullLogger<ErrorReporter>()
        );
        return new NCenFilingReprocessManager(
            new NCenFilingRepository(context),
            _secClient,
            context,
            errorReporter,
            NullLogger<NCenFilingReprocessManager>()
        );
    }
}
