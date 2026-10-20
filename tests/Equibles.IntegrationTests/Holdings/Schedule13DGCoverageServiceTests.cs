using System.Globalization;
using System.Net;
using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Data;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Equibles.Holdings.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.IntegrationTests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Equibles.IntegrationTests.Holdings;

/// <summary>
/// The 13D/13G coverage walk against recorded EDGAR documents: Vanguard's two 2026 13G/As on
/// NVIDIA, which the first backfill lost, and Allspring's same-day 13G and 13G/A on Innospec.
/// </summary>
[Collection(ParadeDbCollection.Name)]
public class Schedule13DGCoverageServiceTests : IAsyncLifetime
{
    private const string NvidiaCusip = "67066G104";
    private const string InnospecCusip = "45768S105";
    private const string VanguardNvidia = "0000102909-26-000226";
    private const string VanguardNvidiaExit = "0000102909-26-000426";
    private const string AllspringOriginal = "0001890906-26-000068";
    private const string AllspringAmendment = "0001890906-26-000074";
    private static readonly DateOnly MinReportDate = new(2020, 1, 1);

    private readonly ParadeDbFixture _fixture;
    private readonly List<EquiblesFinancialDbContext> _contexts = [];
    private readonly CultureInfo _previousCulture;
    private readonly ISecEdgarClient _edgar = Substitute.For<ISecEdgarClient>();
    private readonly Dictionary<DateOnly, List<EdgarDailyIndexEntry>> _index = [];

    public Schedule13DGCoverageServiceTests(ParadeDbFixture fixture)
    {
        _fixture = fixture;
        _previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        _edgar
            .GetDailyIndexForForms(
                Arg.Any<DateOnly>(),
                Arg.Any<IReadOnlyCollection<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => _index.GetValueOrDefault(call.ArgAt<DateOnly>(0)) ?? []);
    }

    public async Task InitializeAsync() => await _fixture.ResetAsync();

    public Task DisposeAsync()
    {
        foreach (var ctx in _contexts)
            ctx.Dispose();
        CultureInfo.CurrentCulture = _previousCulture;
        return Task.CompletedTask;
    }

    [Fact]
    public async Task BackfillDays_ReadsOnlyAccessionsWithNothingOnFile_AndImportsEachEventDate()
    {
        await SeedIssuer("NVDA", "0001045810", NvidiaCusip);
        // Each filing is listed under its filer and under its subject company.
        List(new DateOnly(2026, 1, 30), VanguardNvidia, "102909", "1045810");
        List(new DateOnly(2026, 3, 26), VanguardNvidiaExit, "102909", "1045810");
        List(new DateOnly(2026, 3, 26), "0000000001-26-000001", "1");
        List(new DateOnly(2026, 3, 26), "0000000002-26-000002", "2");
        await SeedDetails("0000000001-26-000001");
        await SeedProcessed("0000000002-26-000002");
        Serve(VanguardNvidia, "sc13ga-vanguard-nvidia.xml");
        Serve(VanguardNvidiaExit, "sc13ga-vanguard-nvidia-exit.xml");

        var result = await Coverage()
            .BackfillDays(
                new DateOnly(2026, 1, 30),
                new DateOnly(2026, 3, 31),
                MinReportDate,
                100,
                CancellationToken.None
            );

        result.Should().Be(new Schedule13DGBackfillResult(new DateOnly(2026, 3, 31), 2, 2, false));
        (await Positions(NvidiaCusip))
            .Should()
            .Equal(
                (VanguardNvidia, new DateOnly(2025, 12, 31), 2_266_625_855L, "102909"),
                (VanguardNvidiaExit, new DateOnly(2026, 3, 13), 0L, "102909")
            );
        await _edgar
            .Received(2)
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task BackfillDays_OriginalFoundAfterItsStoredAmendment_KeepsTheAmendment()
    {
        await SeedIssuer("IOSP", "0001054905", InnospecCusip);
        var amendment = Parse("sc13ga-allspring-innospec.xml", AllspringAmendment);
        (await Ingestion().ImportParsedFiling(amendment, MinReportDate, CancellationToken.None))
            .Should()
            .Be(Schedule13DGImportOutcome.Imported);
        List(new DateOnly(2026, 10, 7), AllspringOriginal, "1890906", "1054905");
        Serve(AllspringOriginal, "sc13g-allspring-innospec.xml");

        await Coverage()
            .BackfillDays(
                new DateOnly(2026, 10, 7),
                new DateOnly(2026, 10, 7),
                MinReportDate,
                100,
                CancellationToken.None
            );

        await using var verify = FreshContext();
        var stored = await verify
            .Set<InstitutionalHolding>()
            .Where(h => h.Cusip == InnospecCusip)
            .SingleAsync();
        stored.AccessionNumber.Should().Be(AllspringAmendment);
        stored.IsAmendment.Should().BeTrue();
        (await verify.Set<ProcessedFiling>().Select(p => p.AccessionNumber).ToListAsync())
            .Should()
            .BeEquivalentTo([AllspringOriginal, AllspringAmendment]);
        (await verify.Set<BeneficialOwnershipFiling>().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task BackfillDays_EdgarUnavailableMidDay_StopsBeforeThatDayAndKeepsItsWork()
    {
        await SeedIssuer("NVDA", "0001045810", NvidiaCusip);
        List(new DateOnly(2026, 1, 30), VanguardNvidia, "102909");
        List(new DateOnly(2026, 3, 26), VanguardNvidiaExit, "102909");
        Serve(VanguardNvidia, "sc13ga-vanguard-nvidia.xml");
        _edgar
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                VanguardNvidiaExit,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(
                new HttpRequestException("throttled", null, HttpStatusCode.TooManyRequests)
            );

        var result = await Coverage()
            .BackfillDays(
                new DateOnly(2026, 1, 30),
                new DateOnly(2026, 3, 31),
                MinReportDate,
                100,
                CancellationToken.None
            );

        result.CompletedThrough.Should().Be(new DateOnly(2026, 3, 25));
        result.Stopped.Should().BeTrue();
        (await Positions(NvidiaCusip)).Select(p => p.Accession).Should().Equal(VanguardNvidia);
        await using var verify = FreshContext();
        (await verify.Set<BeneficialOwnershipFiling>().Select(f => f.AccessionNumber).ToListAsync())
            .Should()
            .Equal(VanguardNvidia);
    }

    [Fact]
    public async Task BackfillDays_StopsAfterTheDayThatReachesTheDocumentBudget()
    {
        await SeedIssuer("NVDA", "0001045810", NvidiaCusip);
        List(new DateOnly(2026, 1, 30), VanguardNvidia, "102909");
        List(new DateOnly(2026, 3, 26), VanguardNvidiaExit, "102909");
        Serve(VanguardNvidia, "sc13ga-vanguard-nvidia.xml");
        Serve(VanguardNvidiaExit, "sc13ga-vanguard-nvidia-exit.xml");

        var result = await Coverage()
            .BackfillDays(
                new DateOnly(2026, 1, 30),
                new DateOnly(2026, 3, 31),
                MinReportDate,
                1,
                CancellationToken.None
            );

        result.Should().Be(new Schedule13DGBackfillResult(new DateOnly(2026, 1, 30), 1, 1, false));
    }

    [Fact]
    public async Task BackfillDays_DocumentEdgarDoesNotServe_IsRecordedOnceAndNotReadAgain()
    {
        List(new DateOnly(2026, 1, 30), VanguardNvidia, "102909");
        _edgar
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                VanguardNvidia,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns([]);

        var day = new DateOnly(2026, 1, 30);
        await Coverage().BackfillDays(day, day, MinReportDate, 100, CancellationToken.None);
        await Coverage().BackfillDays(day, day, MinReportDate, 100, CancellationToken.None);

        await using var verify = FreshContext();
        var detail = await verify.Set<BeneficialOwnershipFiling>().SingleAsync();
        detail.IsUnreadable.Should().BeTrue();
        detail.FilingType.Should().Be(FilingType.Schedule13G);
        await _edgar
            .Received(1)
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task BackfillDays_DocumentEdgarAnswers404_IsRecordedUnreadable()
    {
        List(new DateOnly(2026, 1, 30), VanguardNvidia, "102909");
        _edgar
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                VanguardNvidia,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException("gone", null, HttpStatusCode.NotFound));

        var day = new DateOnly(2026, 1, 30);
        var result = await Coverage()
            .BackfillDays(day, day, MinReportDate, 100, CancellationToken.None);

        result.Should().Be(new Schedule13DGBackfillResult(day, 1, 0, false));
        await using var verify = FreshContext();
        (await verify.Set<BeneficialOwnershipFiling>().SingleAsync())
            .IsUnreadable.Should()
            .BeTrue();
    }

    // A parser fix ships with a CoverageVersion bump, so a document the parser cannot read today
    // must leave nothing on file for that walk to skip.
    [Fact]
    public async Task BackfillDays_UnparseableDocument_StaysRetryableForTheNextWalk()
    {
        await SeedIssuer("NVDA", "0001045810", NvidiaCusip);
        List(new DateOnly(2026, 1, 30), VanguardNvidia, "102909");
        _edgar
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                VanguardNvidia,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Encoding.UTF8.GetBytes("<edgarSubmission><coverPageHeader>"));
        var day = new DateOnly(2026, 1, 30);

        var first = await Coverage()
            .BackfillDays(day, day, MinReportDate, 100, CancellationToken.None);

        first.Should().Be(new Schedule13DGBackfillResult(day, 1, 0, false));
        await using (var verify = FreshContext())
            (await verify.Set<BeneficialOwnershipFiling>().AnyAsync()).Should().BeFalse();

        Serve(VanguardNvidia, "sc13ga-vanguard-nvidia.xml");
        var next = await Coverage()
            .BackfillDays(day, day, MinReportDate, 100, CancellationToken.None);

        next.Should().Be(new Schedule13DGBackfillResult(day, 1, 1, false));
        (await Positions(NvidiaCusip))
            .Should()
            .Equal((VanguardNvidia, new DateOnly(2025, 12, 31), 2_266_625_855L, "102909"));
    }

    [Fact]
    public async Task RetryResolvable_DocumentNoLongerReadable_KeepsItsStoredDetails()
    {
        List(new DateOnly(2026, 1, 30), VanguardNvidia, "102909");
        Serve(VanguardNvidia, "sc13ga-vanguard-nvidia.xml");
        var day = new DateOnly(2026, 1, 30);
        await Coverage().BackfillDays(day, day, MinReportDate, 100, CancellationToken.None);
        await SeedIssuer("NVDA", "0001045810", NvidiaCusip);
        await AgeDetails(TimeSpan.FromDays(2));
        _edgar
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                VanguardNvidia,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .ThrowsAsync(new HttpRequestException("gone", null, HttpStatusCode.NotFound));

        (await Coverage().RetryResolvable(10, TimeSpan.FromDays(1), MinReportDate, default))
            .Should()
            .Be(0);
        (await Coverage().RetryResolvable(10, TimeSpan.FromDays(1), MinReportDate, default))
            .Should()
            .Be(0);

        await using var verify = FreshContext();
        var detail = await verify.Set<BeneficialOwnershipFiling>().SingleAsync();
        detail.IsUnreadable.Should().BeFalse();
        detail.IssuerCusip.Should().Be(NvidiaCusip);
        // The failed read restarted the backoff, so the second retry did not fetch it again.
        await _edgar
            .Received(2)
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                VanguardNvidia,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RetryResolvable_FilingReadBeforeItsIssuerWasTracked_ImportsOnceItIs()
    {
        List(new DateOnly(2026, 1, 30), VanguardNvidia, "102909");
        Serve(VanguardNvidia, "sc13ga-vanguard-nvidia.xml");
        var day = new DateOnly(2026, 1, 30);
        await Coverage().BackfillDays(day, day, MinReportDate, 100, CancellationToken.None);
        (await Positions(NvidiaCusip)).Should().BeEmpty();
        await AgeDetails(TimeSpan.FromDays(2));

        // Untracked, the stored details never cost another document read.
        (await Coverage().RetryResolvable(10, TimeSpan.FromDays(1), MinReportDate, default))
            .Should()
            .Be(0);
        await SeedIssuer("NVDA", "0001045810", NvidiaCusip);
        var imported = await Coverage()
            .RetryResolvable(10, TimeSpan.FromDays(1), MinReportDate, default);

        imported.Should().Be(1);
        (await Positions(NvidiaCusip))
            .Should()
            .Equal((VanguardNvidia, new DateOnly(2025, 12, 31), 2_266_625_855L, "102909"));
        await _edgar
            .Received(2)
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                VanguardNvidia,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task RetryResolvable_FilingReadWithinTheBackoff_WaitsForIt()
    {
        List(new DateOnly(2026, 1, 30), VanguardNvidia, "102909");
        Serve(VanguardNvidia, "sc13ga-vanguard-nvidia.xml");
        var day = new DateOnly(2026, 1, 30);
        await Coverage().BackfillDays(day, day, MinReportDate, 100, CancellationToken.None);
        await SeedIssuer("NVDA", "0001045810", NvidiaCusip);

        (await Coverage().RetryResolvable(10, TimeSpan.FromDays(1), MinReportDate, default))
            .Should()
            .Be(0);
        (await Positions(NvidiaCusip)).Should().BeEmpty();
    }

    [Fact]
    public async Task RetryResolvable_EarlierFilingOfAStoredStake_IsNotReadAgain()
    {
        await SeedIssuer("IOSP", "0001054905", InnospecCusip);
        var original = Parse("sc13g-allspring-innospec.xml", AllspringOriginal);
        await Writer().Write(original, CancellationToken.None);
        var amendment = Parse("sc13ga-allspring-innospec.xml", AllspringAmendment);
        await Ingestion().ImportParsedFiling(amendment, MinReportDate, CancellationToken.None);
        await AgeDetails(TimeSpan.FromDays(2));

        (await Coverage().RetryResolvable(10, TimeSpan.FromDays(1), MinReportDate, default))
            .Should()
            .Be(0);
        await _edgar
            .DidNotReceive()
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task BackfillDays_PreXmlFiling_ImportsTheLeadPositionFromTheCompleteSubmission()
    {
        const string blackRock = "0001086364-24-004712";
        await SeedIssuer("NVDA", "0001045810", NvidiaCusip);
        ListForm(new DateOnly(2024, 1, 26), blackRock, "SC 13G/A", "1045810", "1364742");
        ServeSubmission(blackRock, "sc13ga-text-blackrock-nvidia.txt");

        var result = await Coverage()
            .BackfillDays(
                new DateOnly(2024, 1, 26),
                new DateOnly(2024, 1, 26),
                MinReportDate,
                100,
                CancellationToken.None
            );

        result.Should().Be(new Schedule13DGBackfillResult(new DateOnly(2024, 1, 26), 1, 1, false));
        (await Positions(NvidiaCusip))
            .Should()
            .Equal((blackRock, new DateOnly(2023, 12, 31), 180_593_555L, "1364742"));
        (await SubmissionReads(blackRock)).Should().Be(1);
        await using var verify = FreshContext();
        var details = await verify
            .Set<BeneficialOwnershipFiling>()
            .Include(f => f.ReportingPersons)
            .SingleAsync(f => f.AccessionNumber == blackRock);
        details.ParserVersion.Should().Be(Filing13DGTextParser.Version);
        details.RefusalReason.Should().BeNull();
        details.Rules.Should().Be(Schedule13GRules.QualifiedInstitution);
        details.ReportingPersons.Should().ContainSingle().Which.Cik.Should().Be("1364742");
    }

    [Fact]
    public async Task BackfillDays_RefusedPreXmlFiling_IsParkedUntilANewerReaderVersion()
    {
        const string fmr = "0000315066-24-001331";
        await SeedIssuer("NVDA", "0001045810", NvidiaCusip);
        ListForm(new DateOnly(2024, 2, 9), fmr, "SC 13G/A", "1045810", "315066");
        ServeSubmission(fmr, "sc13ga-text-fmr-nvidia-row-numbers.txt");
        var day = new DateOnly(2024, 2, 9);

        await Coverage().BackfillDays(day, day, MinReportDate, 100, CancellationToken.None);
        await Coverage().BackfillDays(day, day, MinReportDate, 100, CancellationToken.None);

        (await SubmissionReads(fmr)).Should().Be(1);
        (await Positions(NvidiaCusip)).Should().BeEmpty();
        await using (var verify = FreshContext())
        {
            var parked = await verify
                .Set<BeneficialOwnershipFiling>()
                .Include(f => f.ReportingPersons)
                .SingleAsync(f => f.AccessionNumber == fmr);
            parked.IsUnreadable.Should().BeTrue();
            parked.RefusalReason.Should().NotBeNullOrEmpty();
            parked.ParserVersion.Should().Be(Filing13DGTextParser.Version);
            parked.FilingType.Should().Be(FilingType.Schedule13G);
            parked.FilerCik.Should().Be("315066");
            parked.IssuerCik.Should().Be("1045810");
            parked.ReportingPersons.Should().BeEmpty();
        }

        // A refusal parked under an older reader is read again by the next walk.
        await using (var age = FreshContext())
            await age.Set<BeneficialOwnershipFiling>()
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(f => f.ParserVersion, Filing13DGTextParser.Version - 1)
                );
        await Coverage().BackfillDays(day, day, MinReportDate, 100, CancellationToken.None);

        (await SubmissionReads(fmr)).Should().Be(2);
    }

    // NVIDIA's shares outstanding, which Vanguard's 2,266,625,855 shares at 9.32% agree with.
    private const long NvidiaSharesOutstanding = 24_300_000_000;

    [Theory]
    [InlineData(null)]
    [InlineData("000000000")]
    [InlineData("67066G105")]
    [InlineData("N/A")]
    public async Task ImportParsedFiling_NoUsableCusip_MapsThroughTheSubjectCompanysOnlyCommonSecurity(
        string filedCusip
    )
    {
        await SeedCommonIssuer("NVDA", "1045810", NvidiaCusip, NvidiaSharesOutstanding);
        var filing = ParseVanguardNvidia();
        filing.IssuerCusip = filedCusip;

        (await Ingestion().ImportParsedFiling(filing, MinReportDate, CancellationToken.None))
            .Should()
            .Be(Schedule13DGImportOutcome.Imported);

        (await Positions(NvidiaCusip))
            .Should()
            .Equal((VanguardNvidia, new DateOnly(2025, 12, 31), 2_266_625_855L, "102909"));
        await using var verify = FreshContext();
        // The details keep the CUSIP as filed.
        (await verify.Set<BeneficialOwnershipFiling>().SingleAsync())
            .IssuerCusip.Should()
            .Be(filedCusip);
    }

    [Theory]
    [InlineData(ListedSecurityType.CommonShares, "NVDAB")]
    [InlineData(ListedSecurityType.Unknown, null)]
    [InlineData(ListedSecurityType.PreferredShares, null)]
    public async Task ImportParsedFiling_SubjectCompanyWithoutOneCertainCommonSecurity_StaysUnresolved(
        ListedSecurityType registration,
        string siblingTicker
    )
    {
        await SeedCommonIssuer(
            "NVDA",
            "1045810",
            NvidiaCusip,
            NvidiaSharesOutstanding,
            registration,
            siblingTicker == null ? [] : [siblingTicker]
        );
        var filing = ParseVanguardNvidia();
        filing.IssuerCusip = "000000000";

        (await Ingestion().ImportParsedFiling(filing, MinReportDate, CancellationToken.None))
            .Should()
            .Be(Schedule13DGImportOutcome.Incomplete);

        await using var verify = FreshContext();
        (await verify.Set<InstitutionalHolding>().AnyAsync()).Should().BeFalse();
    }

    // A tenth of the shares is what a depositary share standing for ten ordinary shares looks like;
    // no shares outstanding, or no stated percent, cannot confirm the security at all.
    [Theory]
    [InlineData(2_430_000_000L, false)]
    [InlineData(0L, false)]
    [InlineData(NvidiaSharesOutstanding, true)]
    public async Task ImportParsedFiling_AmountThatDisagreesWithTheSecuritysSharesOutstanding_StaysUnresolved(
        long sharesOutstanding,
        bool dropPercent
    )
    {
        await SeedCommonIssuer("NVDA", "1045810", NvidiaCusip, sharesOutstanding);
        var filing = ParseVanguardNvidia();
        filing.IssuerCusip = null;
        if (dropPercent)
            filing.ReportingPersons.ForEach(person => person.PercentOfClass = null);

        (await Ingestion().ImportParsedFiling(filing, MinReportDate, CancellationToken.None))
            .Should()
            .Be(Schedule13DGImportOutcome.Incomplete);

        await using var verify = FreshContext();
        (await verify.Set<InstitutionalHolding>().AnyAsync()).Should().BeFalse();
    }

    // Real filings on a security the directory does not hold, whose issuer's one tracked security
    // is its common line: ordinary shares behind a depositary share (IperionX, VNET) and a
    // preferred (PIMCO Municipal Income Fund II). A well-formed filed CUSIP that resolves to
    // nothing is never re-pointed at the subject company, now or on a later retry.
    [Theory]
    [InlineData(
        "sc13ga-fmr-iperionx-ordinary-shares.xml",
        "0000315066-25-000774",
        "315066",
        "IPX",
        "1898601",
        "44916E100",
        36_224_016L
    )]
    [InlineData(
        "sc13da-vnet-class-a-ordinary-shares.xml",
        "0000950170-25-042308",
        "1758419",
        "VNET",
        "1508475",
        "90138A103",
        285_233_278L
    )]
    [InlineData(
        "sc13da-pimco-municipal-preferred.xml",
        "0001140361-24-050098",
        "70858",
        "PML",
        "1170299",
        "72200W106",
        125_468_000L
    )]
    public async Task ImportParsedFiling_WellFormedCusipOfAnUntrackedSecurity_StaysUnresolved(
        string asset,
        string accession,
        string filerCik,
        string ticker,
        string issuerCik,
        string commonCusip,
        long sharesOutstanding
    )
    {
        await SeedCommonIssuer(ticker, issuerCik, commonCusip, sharesOutstanding);
        var filing = new Filing13DGXmlParser().ParseFiling(
            Asset(asset),
            accession,
            filerCik,
            new DateOnly(2025, 3, 20)
        );
        filing.IssuerCik.Should().Be(issuerCik);
        filing.IssuerCusip.Should().NotBe(commonCusip);

        (await Ingestion().ImportParsedFiling(filing, MinReportDate, CancellationToken.None))
            .Should()
            .Be(Schedule13DGImportOutcome.Incomplete);
        await AgeDetails(TimeSpan.FromDays(2));
        (await Coverage().RetryResolvable(10, TimeSpan.FromDays(1), MinReportDate, default))
            .Should()
            .Be(0);

        await using var verify = FreshContext();
        (await verify.Set<InstitutionalHolding>().AnyAsync()).Should().BeFalse();
        _edgar.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ImportParsedFiling_AmendmentMappedThroughTheSubjectCompany_RestatesOnlyThatSecurity()
    {
        await SeedCommonIssuer("NVDA", "1045810", NvidiaCusip, NvidiaSharesOutstanding);
        await SeedCommonIssuer("IOSP", "1054905", InnospecCusip, 25_000_000);
        // The same filer's same-day stake in another security must survive the restatement.
        var other = ParseVanguardNvidia();
        other.AccessionNumber = "0000102909-26-000001";
        other.IsAmendment = false;
        other.IssuerCik = "1054905";
        other.IssuerCusip = InnospecCusip;
        (await Ingestion().ImportParsedFiling(other, MinReportDate, CancellationToken.None))
            .Should()
            .Be(Schedule13DGImportOutcome.Imported);
        var amendment = ParseVanguardNvidia();
        amendment.IssuerCusip = "000000000";

        (await Ingestion().ImportParsedFiling(amendment, MinReportDate, CancellationToken.None))
            .Should()
            .Be(Schedule13DGImportOutcome.Imported);

        (await Positions(InnospecCusip)).Should().ContainSingle();
        (await Positions(NvidiaCusip)).Should().ContainSingle();
    }

    [Fact]
    public async Task BackfillDays_PreXmlFilingWithoutACusip_MapsThroughTheSubjectCompany()
    {
        const string blackRock = "0001086364-24-003191";
        const string sunOptaCusip = "8676EP108";
        // 9,984,820 shares at 8.4%.
        await SeedCommonIssuer("STKL", "351834", sunOptaCusip, 118_000_000);
        ListForm(new DateOnly(2024, 1, 25), blackRock, "SC 13G/A", "351834", "1364742");
        ServeSubmission(blackRock, "sc13ga-text-blackrock-sedol.txt");

        await Coverage()
            .BackfillDays(
                new DateOnly(2024, 1, 25),
                new DateOnly(2024, 1, 25),
                MinReportDate,
                100,
                CancellationToken.None
            );

        (await Positions(sunOptaCusip))
            .Should()
            .Equal((blackRock, new DateOnly(2023, 12, 31), 9_984_820L, "1364742"));
    }

    [Fact]
    public async Task RetryResolvable_PreXmlFilingReadBeforeItsSubjectCompanyWasTracked_ImportsOnceItIs()
    {
        const string bulldog = "0001398344-24-000853";
        const string texasPacificCusip = "88262P102";
        await SeedIssuer("NVDA", "0001045810", NvidiaCusip);
        ListForm(new DateOnly(2024, 1, 22), bulldog, "SC 13D", "1811074", "1504304");
        ServeSubmission(bulldog, "sc13d-text-bulldog.txt");
        var day = new DateOnly(2024, 1, 22);
        await Coverage().BackfillDays(day, day, MinReportDate, 100, CancellationToken.None);
        (await Positions(texasPacificCusip)).Should().BeEmpty();

        // 4,502 shares at 0.06%.
        await SeedCommonIssuer("TPL", "1811074", texasPacificCusip, 7_690_000);
        await AgeDetails(TimeSpan.FromDays(2));
        var imported = await Coverage()
            .RetryResolvable(10, TimeSpan.FromDays(1), MinReportDate, CancellationToken.None);

        imported.Should().Be(1);
        (await SubmissionReads(bulldog)).Should().Be(2);
        await using var verify = FreshContext();
        var stored = await verify
            .Set<InstitutionalHolding>()
            .Include(h => h.InstitutionalHolder)
            .SingleAsync();
        stored.Cusip.Should().Be(texasPacificCusip);
        stored.FilingType.Should().Be(FilingType.Schedule13D);
        stored.ReportDate.Should().Be(new DateOnly(2024, 1, 17));
        stored.Shares.Should().Be(4_502);
        stored.InstitutionalHolder.Cik.Should().Be("1504304");
    }

    private Parsed13DGFiling ParseVanguardNvidia() =>
        new Filing13DGXmlParser().ParseFiling(
            Asset("sc13ga-vanguard-nvidia.xml"),
            VanguardNvidia,
            "102909",
            new DateOnly(2026, 1, 30)
        );

    private void List(DateOnly day, string accession, params string[] ciks) =>
        ListForm(day, accession, "SCHEDULE 13G/A", ciks);

    private void ListForm(DateOnly day, string accession, string form, params string[] ciks)
    {
        if (!_index.TryGetValue(day, out var entries))
            _index[day] = entries = [];
        entries.AddRange(
            ciks.Select(cik => new EdgarDailyIndexEntry
            {
                FormType = form,
                CompanyName = $"Company {cik}",
                Cik = cik,
                DateFiled = day,
                AccessionNumber = accession,
            })
        );
    }

    private void Serve(string accession, string asset) =>
        _edgar
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                accession,
                "primary_doc.xml",
                Arg.Any<CancellationToken>()
            )
            .Returns(Encoding.UTF8.GetBytes(Asset(asset)));

    // A pre-XML filing is read from its complete submission, as EDGAR serves {accession}.txt.
    private void ServeSubmission(string accession, string asset) =>
        _edgar
            .GetDocumentFileBytes(
                Arg.Any<string>(),
                accession,
                $"{accession}.txt",
                Arg.Any<CancellationToken>()
            )
            .Returns(Encoding.UTF8.GetBytes(Asset(asset)));

    private Task<int> SubmissionReads(string accession) =>
        Task.FromResult(
            _edgar
                .ReceivedCalls()
                .Count(call =>
                    call.GetMethodInfo().Name == nameof(ISecEdgarClient.GetDocumentFileBytes)
                    && (string)call.GetArguments()[2] == $"{accession}.txt"
                )
        );

    private static string Asset(string name) =>
        File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Holdings13DG", name)
        );

    private static Parsed13DGFiling Parse(string asset, string accession) =>
        new Filing13DGXmlParser().ParseFiling(
            Asset(asset),
            accession,
            "1890906",
            new DateOnly(2026, 10, 7)
        );

    // An issuer whose one security is registered as common shares, plus a sibling class for each
    // of siblingTickers.
    private async Task SeedCommonIssuer(
        string ticker,
        string cik,
        string cusip,
        long sharesOutstanding,
        ListedSecurityType registration = ListedSecurityType.CommonShares,
        params string[] siblingTickers
    )
    {
        await using var seed = FreshContext();
        seed.Set<EquityIssuer>()
            .Add(
                Equibles.TestSupport.EquityIssuerSeed.Create(
                    Id: Guid.NewGuid(),
                    Ticker: ticker,
                    Name: ticker,
                    Cik: cik,
                    Cusip: cusip,
                    SharesOutStanding: sharesOutstanding,
                    ListedSecurityType: registration,
                    SecondaryTickers: [.. siblingTickers]
                )
            );
        await seed.SaveChangesAsync();
    }

    private async Task SeedIssuer(string ticker, string cik, string cusip)
    {
        await using var seed = FreshContext();
        seed.Set<EquityIssuer>()
            .Add(
                Equibles.TestSupport.EquityIssuerSeed.Create(
                    Id: Guid.NewGuid(),
                    Ticker: ticker,
                    Name: ticker,
                    Cik: cik,
                    Cusip: cusip
                )
            );
        await seed.SaveChangesAsync();
    }

    private async Task SeedDetails(string accession)
    {
        await using var seed = FreshContext();
        seed.Add(
            new BeneficialOwnershipFiling
            {
                AccessionNumber = accession,
                FilingType = FilingType.Schedule13G,
                FilingDate = new DateOnly(2026, 3, 26),
                IsUnreadable = true,
            }
        );
        await seed.SaveChangesAsync();
    }

    private async Task SeedProcessed(string accession)
    {
        await using var seed = FreshContext();
        seed.Add(new ProcessedFiling { AccessionNumber = accession });
        await seed.SaveChangesAsync();
    }

    private async Task AgeDetails(TimeSpan age)
    {
        await using var ctx = FreshContext();
        var readAt = DateTime.UtcNow - age;
        await ctx.Set<BeneficialOwnershipFiling>()
            .ExecuteUpdateAsync(setters => setters.SetProperty(f => f.ReadAt, readAt));
    }

    private async Task<
        List<(string Accession, DateOnly ReportDate, long Shares, string HolderCik)>
    > Positions(string cusip)
    {
        await using var verify = FreshContext();
        var rows = await verify
            .Set<InstitutionalHolding>()
            .Where(h => h.Cusip == cusip)
            .OrderBy(h => h.ReportDate)
            .Select(h => new
            {
                h.AccessionNumber,
                h.ReportDate,
                h.Shares,
                h.InstitutionalHolder.Cik,
                h.FilingType,
            })
            .ToListAsync();
        rows.Should().OnlyContain(r => r.FilingType == FilingType.Schedule13G);
        return rows.Select(r => (r.AccessionNumber, r.ReportDate, r.Shares, r.Cik)).ToList();
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
                sp.GetService(typeof(EquityIssuerRepository))
                    .Returns(new EquityIssuerRepository(ctx));
                sp.GetService(typeof(InstitutionalHolderRepository))
                    .Returns(new InstitutionalHolderRepository(ctx));
                sp.GetService(typeof(InstitutionalHoldingRepository))
                    .Returns(new InstitutionalHoldingRepository(ctx));
                sp.GetService(typeof(ProcessedFilingRepository))
                    .Returns(new ProcessedFilingRepository(ctx));
                var scope = Substitute.For<IServiceScope>();
                scope.ServiceProvider.Returns(sp);
                return scope;
            });
        return scopeFactory;
    }

    private BeneficialOwnershipDetailWriter Writer() =>
        new(CreateScopeFactory(), Substitute.For<ILogger<BeneficialOwnershipDetailWriter>>());

    private Realtime13DGIngestionService Ingestion()
    {
        var scopeFactory = CreateScopeFactory();
        var prices = Substitute.For<IStockPriceProvider>();
        prices
            .GetClosingPrices(
                Arg.Any<IEnumerable<(Guid, string, DateOnly)>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Task.FromResult(
                    call.ArgAt<IEnumerable<(Guid, string, DateOnly)>>(0)
                        .Distinct()
                        .ToDictionary(key => key, _ => 100m)
                )
            );
        var importService = new HoldingsImportService(
            scopeFactory,
            Substitute.For<ILogger<HoldingsImportService>>(),
            Options.Create(new WorkerOptions()),
            prices,
            Substitute.For<MassTransit.IBus>()
        );
        return new Realtime13DGIngestionService(
            _edgar,
            new Filing13DGXmlParser(),
            new Realtime13DGArchiveBuilder(),
            importService,
            Writer(),
            scopeFactory,
            Substitute.For<ILogger<Realtime13DGIngestionService>>()
        );
    }

    private Schedule13DGCoverageService Coverage() =>
        new(
            _edgar,
            new Filing13DGXmlParser(),
            new Filing13DGTextParser(),
            Ingestion(),
            Writer(),
            CreateScopeFactory(),
            Substitute.For<ILogger<Schedule13DGCoverageService>>()
        );
}
