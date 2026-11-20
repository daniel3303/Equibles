using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Data;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// Anthropic traded on its first day while SEC's directory still listed ANT with a blank exchange,
/// and a company delisted in 2006 still held ANT on an inactive listing. The sync creates the new
/// registrant from its filed listing evidence, and the old inactive holder neither blocks it nor
/// changes.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class CompanySyncServiceListingEvidenceTests : ParadeDbMcpTestBase
{
    private const string NewCik = "2130357";
    private const string OldCik = "1163842";

    public CompanySyncServiceListingEvidenceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task BlankExchangeRegistrantWithListingEvidence_IsCreatedBesideAnInactiveHolder()
    {
        var old = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: OldCik,
            Ticker: "ANT",
            Name: "Anteon International Corporation",
            Active: false,
            DelistedOn: new DateOnly(2006, 6, 9)
        );
        DbContext.Add(old);
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();

        // Filings dated from the run's own day: the rule admits only a recent final prospectus.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar.GetActiveCompanies().Returns(new List<CompanyInfo>());
        edgar
            .GetUnlistedTickerRegistrants()
            .Returns(
                new List<CompanyInfo>
                {
                    new()
                    {
                        Cik = NewCik,
                        Name = "Anthropic, PBC",
                        Tickers = ["ANT"],
                    },
                }
            );
        edgar
            .GetCompanyFilings(
                NewCik,
                Arg.Any<string>(),
                Arg.Any<DateOnly?>(),
                Arg.Any<DateOnly?>()
            )
            .Returns(
                new List<FilingData>
                {
                    Filing("8-A12B", today.AddDays(-2), "0001628280-26-074342"),
                    Filing("CERT", today.AddDays(-1), "0001354457-26-001088"),
                    Filing("424B4", today, "0001628280-26-074944"),
                }
            );

        await BuildSut(edgar).SyncCompaniesFromSecApi();

        await using var verify = Fixture.CreateDbContext();
        var issuers = await verify.Set<EquityIssuer>().AsNoTracking().ToListAsync();
        var created = issuers.Should().ContainSingle(issuer => issuer.Cik == NewCik).Which;
        created
            .Presentation.Listing.Should()
            .Match<EquityListing>(listing =>
                listing.Ticker == "ANT" && listing.Active && listing.MarketCountryCode == "US"
            );
        issuers
            .Should()
            .ContainSingle(issuer => issuer.Cik == OldCik)
            .Which.Presentation.Listing.Active.Should()
            .BeFalse();
        (await verify.Set<SecListingEvidenceCheck>().AsNoTracking().SingleAsync())
            .Outcome.Should()
            .Be(SecListingEvidenceOutcome.Admitted);
    }

    // Before it listed, the registrant was followed for its registration statements only. Its admission
    // gives that same record the ticker, so the filings stored under it carry over, and drops its sync
    // stamp so the scraper reads its other forms at once.
    [Fact]
    public async Task PreListingRecord_TakesItsTickerAtAdmission_AndIsReadInFullAtOnce()
    {
        var old = Equibles.TestSupport.EquityIssuerSeed.Create(
            Cik: OldCik,
            Ticker: "ANT",
            Name: "Anteon International Corporation",
            Active: false,
            DelistedOn: new DateOnly(2006, 6, 9)
        );
        var registrant = new EquityIssuer { Name = "Anthropic, PBC", Cik = NewCik };
        DbContext.AddRange(old, registrant);
        DbContext.Add(
            new CompanyFilingSyncState
            {
                EquityIssuerId = registrant.Id,
                LastSyncedAt = DateTime.UtcNow,
            }
        );
        await DbContext.SaveChangesAsync();
        DbContext.ChangeTracker.Clear();
        var edgar = AdmittingEdgar(DateOnly.FromDateTime(DateTime.UtcNow));

        await BuildSut(edgar).SyncCompaniesFromSecApi();

        await using var verify = Fixture.CreateDbContext();
        var issuers = await verify.Set<EquityIssuer>().AsNoTracking().ToListAsync();
        var listed = issuers.Should().ContainSingle(issuer => issuer.Cik == NewCik).Which;
        listed.Id.Should().Be(registrant.Id);
        listed
            .Presentation.Listing.Should()
            .Match<EquityListing>(listing =>
                listing.Ticker == "ANT" && listing.Active && listing.MarketCountryCode == "US"
            );
        issuers
            .Should()
            .ContainSingle(issuer => issuer.Cik == OldCik)
            .Which.Presentation.Listing.Active.Should()
            .BeFalse();
        (
            await verify
                .Set<CompanyFilingSyncState>()
                .AnyAsync(stamp => stamp.EquityIssuerId == registrant.Id)
        )
            .Should()
            .BeFalse();
    }

    private static ISecEdgarClient AdmittingEdgar(DateOnly today)
    {
        var edgar = Substitute.For<ISecEdgarClient>();
        edgar.GetActiveCompanies().Returns(new List<CompanyInfo>());
        edgar
            .GetUnlistedTickerRegistrants()
            .Returns(
                new List<CompanyInfo>
                {
                    new()
                    {
                        Cik = NewCik,
                        Name = "Anthropic, PBC",
                        Tickers = ["ANT"],
                    },
                }
            );
        edgar
            .GetCompanyFilings(
                NewCik,
                Arg.Any<string>(),
                Arg.Any<DateOnly?>(),
                Arg.Any<DateOnly?>()
            )
            .Returns(
                new List<FilingData>
                {
                    Filing("8-A12B", today.AddDays(-2), "0001628280-26-074342"),
                    Filing("CERT", today.AddDays(-1), "0001354457-26-001088"),
                    Filing("424B4", today, "0001628280-26-074944"),
                }
            );
        return edgar;
    }

    private static FilingData Filing(string form, DateOnly filedOn, string accession) =>
        new()
        {
            Cik = NewCik,
            Form = form,
            FilingDate = filedOn,
            AccessionNumber = accession,
        };

    private CompanySyncService BuildSut(ISecEdgarClient edgar)
    {
        var scopeFactory = ServiceScopeSubstitute.Create(
            (typeof(EquityIssuerRepository), new EquityIssuerRepository(DbContext)),
            (
                typeof(EquityIdentityManager),
                new EquityIdentityManager(
                    new EquityIssuerRepository(DbContext),
                    Substitute.For<IBus>()
                )
            ),
            (typeof(EquiblesFinancialDbContext), DbContext)
        );
        return new CompanySyncService(
            scopeFactory,
            edgar,
            Options.Create(new WorkerOptions { TickersToSync = [] }),
            Substitute.For<ILogger<CompanySyncService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            ),
            Substitute.For<IBus>()
        );
    }
}
