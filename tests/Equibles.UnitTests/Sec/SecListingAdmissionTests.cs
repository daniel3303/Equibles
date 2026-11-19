using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.Data;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.HostedService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// A blank-exchange registrant enters the company sync only while its stored listing evidence
/// admits it; EDGAR is read again at most once per recheck interval, and an unreadable index
/// keeps the outcome already stored.
/// </summary>
public class SecListingAdmissionTests
{
    private static readonly DateTime Now = new(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CompanyInfo Anthropic = new()
    {
        Cik = "2130357",
        Name = "Anthropic, PBC",
        Tickers = ["ANT"],
    };

    private static readonly CompanyInfo AfterNext = new()
    {
        Cik = "2087035",
        Name = "AfterNext Acquisition I Corp.",
        Tickers = ["AFNX"],
    };

    private static EquiblesFinancialDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            new IModuleConfiguration[] { new CommonStocksModuleConfiguration() }
        );

    private static async Task<ISecEdgarClient> RecordedEdgar(params string[] ciks)
    {
        var edgar = Substitute.For<ISecEdgarClient>();
        foreach (var cik in ciks)
        {
            var filings = await SecListingEvidenceTests.RecordedFilings(cik, null);
            edgar
                .GetCompanyFilings(cik, Arg.Any<string>(), Arg.Any<DateOnly?>(), Arg.Any<DateOnly?>())
                .Returns(filings);
        }
        return edgar;
    }

    private static Task<List<CompanyInfo>> Admit(
        EquiblesFinancialDbContext db,
        ISecEdgarClient edgar,
        DateTime now,
        params CompanyInfo[] unlisted
    ) => SecListingAdmission.Admit(db, edgar, unlisted, now, NullLogger.Instance);

    [Fact]
    public async Task Admit_OnlyTheRegistrantWithListingEvidence_AndRecordsEveryRead()
    {
        await using var db = NewDb();
        var edgar = await RecordedEdgar("2130357", "2087035");

        var admitted = await Admit(db, edgar, Now, Anthropic, AfterNext);

        admitted.Should().ContainSingle().Which.Cik.Should().Be("2130357");
        var checks = await db.Set<SecListingEvidenceCheck>().ToDictionaryAsync(c => c.Cik);
        checks["2087035"].Outcome.Should().Be(SecListingEvidenceOutcome.NoFinalProspectus);
        checks["2130357"]
            .Should()
            .BeEquivalentTo(
                new SecListingEvidenceCheck
                {
                    Cik = "2130357",
                    Tickers = ["ANT"],
                    CheckedAt = Now,
                    Outcome = SecListingEvidenceOutcome.Admitted,
                    RegistrationAccessionNumber = "0001628280-26-074342",
                    CertificationAccessionNumber = "0001354457-26-001088",
                    ProspectusAccessionNumber = "0001628280-26-074944",
                    ProspectusFiledOn = new DateOnly(2026, 11, 19),
                }
            );
    }

    [Fact]
    public async Task Admit_WithinTheRecheckInterval_ReusesTheStoredOutcome()
    {
        await using var db = NewDb();
        var edgar = await RecordedEdgar("2130357");
        await Admit(db, edgar, Now, Anthropic);
        edgar.ClearReceivedCalls();

        var admitted = await Admit(db, edgar, Now.AddMinutes(30), Anthropic);

        admitted.Should().ContainSingle();
        await edgar
            .DidNotReceiveWithAnyArgs()
            .GetCompanyFilings(default, default, default, default);
    }

    [Fact]
    public async Task Admit_UnreadableIndex_KeepsTheStoredAdmission()
    {
        await using var db = NewDb();
        await Admit(db, await RecordedEdgar("2130357"), Now, Anthropic);
        var failing = Substitute.For<ISecEdgarClient>();
        failing
            .GetCompanyFilings(default, default, default, default)
            .ReturnsForAnyArgs(Task.FromException<List<FilingData>>(new HttpRequestException("503")));

        var later = Now.AddHours(2);
        var admitted = await Admit(db, failing, later, Anthropic, AfterNext);

        admitted.Should().ContainSingle().Which.Cik.Should().Be("2130357");
        (await db.Set<SecListingEvidenceCheck>().SingleAsync())
            .CheckedAt.Should()
            .Be(Now, "a failed read is not a check");
    }

    [Fact]
    public async Task Admit_AfterTheWindow_LapsesWithoutAnotherRead()
    {
        await using var db = NewDb();
        await Admit(db, await RecordedEdgar("2130357"), Now, Anthropic);
        var check = await db.Set<SecListingEvidenceCheck>().SingleAsync();
        check.ProspectusFiledOn = DateOnly.FromDateTime(Now).AddDays(-31);
        await db.SaveChangesAsync();

        var admitted = await Admit(db, Substitute.For<ISecEdgarClient>(), Now.AddMinutes(10), Anthropic);

        admitted.Should().BeEmpty();
    }
}
