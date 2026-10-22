using System.Net;
using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Equibles.Sec.HostedService.Services;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// The registrant profile refresh reads every followed issuer's EDGAR header: never-read issuers
/// first, then profiles older than the refresh age, a 404 or another CIK's reply stamped so it
/// cannot lead the queue forever, an outage never stamped, and an issuer whose archived pages
/// were not read this run left due.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class IssuerRegistrantProfileRefreshServiceTests : ParadeDbMcpTestBase
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private readonly ISecEdgarClient _edgar = Substitute.For<ISecEdgarClient>();

    public IssuerRegistrantProfileRefreshServiceTests(ParadeDbFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task NeverReadIssuer_LeadsTheQueue_ThenProfilesPastTheRefreshAge()
    {
        var stale = await SeedIssuer("0000000101", readOn: Today.AddDays(-40));
        var neverRead = await SeedIssuer("0000000102", readOn: null);
        var fresh = await SeedIssuer("0000000103", readOn: Today.AddDays(-1));
        foreach (var cik in new[] { "0000000101", "0000000102", "0000000103" })
            _edgar.GetCompanyMetadata(cik).Returns(Header(cik, sic: "3711"));

        (await Sut().Run(1, 10, CancellationToken.None)).Should().Be(1);
        await _edgar.Received(1).GetCompanyMetadata("0000000102");
        (await Stored(neverRead.Id))
            .Should()
            .BeEquivalentTo(
                new
                {
                    StateOfIncorporation = "DE",
                    BusinessStateOrCountry = "TX",
                    PeriodicReportFileNumber = "001-00001",
                    SecRegistrantProfileReadOn = (DateOnly?)Today,
                }
            );

        await Sut().Run(10, 10, CancellationToken.None);
        await _edgar.Received(1).GetCompanyMetadata("0000000101");
        await _edgar.DidNotReceive().GetCompanyMetadata("0000000103");
        (await Stored(stale.Id)).SecRegistrantProfileReadOn.Should().Be(Today);
        (await Stored(fresh.Id)).SecRegistrantProfileReadOn.Should().Be(Today.AddDays(-1));
    }

    [Fact]
    public async Task IssuerWithoutATradingListing_IsNeverRead()
    {
        await SeedIssuer("0000000201", readOn: null, active: false);

        (await Sut().Run(10, 10, CancellationToken.None)).Should().Be(0);

        await _edgar.DidNotReceive().GetCompanyMetadata(Arg.Any<string>());
    }

    [Fact]
    public async Task NotFound_StampsTheReadDateAndKeepsTheStoredProfile()
    {
        var issuer = await SeedIssuer("0000000301", readOn: null, incorporation: "DE");
        _edgar
            .GetCompanyMetadata("0000000301")
            .ThrowsAsync(new HttpRequestException("gone", null, HttpStatusCode.NotFound));

        await Sut().Run(10, 10, CancellationToken.None);

        (await Stored(issuer.Id))
            .Should()
            .BeEquivalentTo(
                new { StateOfIncorporation = "DE", SecRegistrantProfileReadOn = (DateOnly?)Today }
            );
    }

    [Fact]
    public async Task SecOutage_EndsTheRunWithoutStampingAnything()
    {
        var first = await SeedIssuer("0000000401", readOn: null);
        var second = await SeedIssuer("0000000402", readOn: null);
        _edgar
            .GetCompanyMetadata(Arg.Any<string>())
            .ThrowsAsync(new HttpRequestException("down", null, HttpStatusCode.ServiceUnavailable));

        (await Sut().Run(10, 10, CancellationToken.None)).Should().Be(0);

        await _edgar.Received(1).GetCompanyMetadata(Arg.Any<string>());
        (await Stored(first.Id)).SecRegistrantProfileReadOn.Should().BeNull();
        (await Stored(second.Id)).SecRegistrantProfileReadOn.Should().BeNull();
    }

    [Fact]
    public async Task ReplyNamingAnotherCik_IsStampedButNotStored()
    {
        var issuer = await SeedIssuer("0000000501", readOn: null);
        _edgar.GetCompanyMetadata("0000000501").Returns(Header("0000000999", sic: "3711"));

        (await Sut().Run(10, 10, CancellationToken.None)).Should().Be(0);

        (await Stored(issuer.Id))
            .Should()
            .BeEquivalentTo(
                new
                {
                    StateOfIncorporation = (string)null,
                    SecRegistrantProfileReadOn = (DateOnly?)Today,
                }
            );
    }

    [Fact]
    public async Task ArchiveReadsBeyondTheCap_StoreTheHeaderAndStayDue()
    {
        var first = await SeedIssuer("0000000601", readOn: null);
        var second = await SeedIssuer("0000000602", readOn: null);
        foreach (var cik in new[] { "0000000601", "0000000602" })
        {
            var header = Header(cik, sic: "");
            header.HasArchivedFilings = true;
            _edgar.GetCompanyMetadata(cik).Returns(header);
            _edgar
                .GetCompanyFilings(cik)
                .Returns([
                    new FilingData { Form = "N-54A", FilingDate = new DateOnly(2004, 2, 3) },
                ]);
        }

        (await Sut().Run(10, 1, CancellationToken.None)).Should().Be(2);

        await _edgar.Received(1).GetCompanyFilings(Arg.Any<string>());
        var profiles = new[] { await Stored(first.Id), await Stored(second.Id) };
        profiles.Should().OnlyContain(profile => profile.StateOfIncorporation == "DE");
        var walked = profiles.Single(profile => profile.BdcHistoryReadOn != null);
        walked.BdcElectionFiledOn.Should().Be(new DateOnly(2004, 2, 3));
        walked.SecRegistrantProfileReadOn.Should().Be(Today);
        profiles
            .Single(profile => profile.BdcHistoryReadOn == null)
            .SecRegistrantProfileReadOn.Should()
            .BeNull();
    }

    [Fact]
    public async Task FailedArchiveRead_DatesTheReadSoTheIssuerDoesNotLeadTheQueue()
    {
        var issuer = await SeedIssuer("0000000701", readOn: null);
        var header = Header("0000000701", sic: "");
        header.HasArchivedFilings = true;
        _edgar.GetCompanyMetadata("0000000701").Returns(header);
        _edgar
            .GetCompanyFilings("0000000701")
            .ThrowsAsync(new InvalidOperationException("malformed archive page"));

        await Sut().Run(10, 10, CancellationToken.None);

        (await Stored(issuer.Id))
            .Should()
            .BeEquivalentTo(
                new
                {
                    StateOfIncorporation = "DE",
                    BdcHistoryReadOn = (DateOnly?)null,
                    SecRegistrantProfileReadOn = (DateOnly?)Today,
                }
            );
    }

    private IssuerRegistrantProfileRefreshService Sut()
    {
        var repository = new EquityIssuerRepository(DbContext);
        var reader = new SecRegistrantProfileReader(
            _edgar,
            new EquityIdentityManager(repository, Substitute.For<IBus>()),
            NullLogger<SecRegistrantProfileReader>()
        );
        return new IssuerRegistrantProfileRefreshService(
            _edgar,
            repository,
            reader,
            Options.Create(new WorkerOptions()),
            NullLogger<IssuerRegistrantProfileRefreshService>()
        );
    }

    private static CompanyMetadata Header(string filerCik, string sic) =>
        new()
        {
            FilerCik = filerCik,
            Sic = sic,
            StateOfIncorporation = "DE",
            BusinessStateOrCountry = "TX",
            PeriodicReportFileNumber = "001-00001",
        };

    private async Task<EquityIssuer> SeedIssuer(
        string cik,
        DateOnly? readOn,
        bool active = true,
        string incorporation = null
    )
    {
        var issuer = Equibles.TestSupport.EquityIssuerSeed.Create(
            Ticker: "T" + cik[^3..],
            Name: "Issuer " + cik,
            Cik: cik,
            Active: active
        );
        issuer.SecRegistrantProfileReadOn = readOn;
        issuer.StateOfIncorporation = incorporation;
        DbContext.Add(issuer);
        await DbContext.SaveChangesAsync();
        return issuer;
    }

    private async Task<EquityIssuer> Stored(Guid id)
    {
        await using var verify = Fixture.CreateDbContext();
        return await verify.Set<EquityIssuer>().AsNoTracking().SingleAsync(i => i.Id == id);
    }
}
