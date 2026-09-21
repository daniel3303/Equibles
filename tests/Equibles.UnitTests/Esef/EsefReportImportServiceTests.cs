using Equibles.CommonStocks.BusinessLogic;
using Equibles.CommonStocks.Data;
using Equibles.CommonStocks.Data.Models;
using Equibles.CommonStocks.Repositories;
using Equibles.Data;
using Equibles.Integrations.XbrlFilings;
using Equibles.Media.Data;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Models;
using Equibles.Sec.HostedService.Services;
using Equibles.Sec.Repositories;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Esef;

// The whole corpus is read and matched by legal entity identifier; the fixture is TotalEnergies' own eight
// filings, which hold the FR/GB pair the country rule exists for. See TestAssets/Esef/README.md.
public class EsefReportImportServiceTests
{
    private const string Lei = "529900S21EQ1BO4ESM68";
    private const string SecondLei = "259400NFU8A8SBP6VC21";
    private const string LatestFrenchJson =
        "/529900S21EQ1BO4ESM68/2025-12-31/ESEF/FR/0/529900S21EQ1BO4ESM68-2025-12-31-1-fr.json";
    private const string LatestFrenchReport =
        "/529900S21EQ1BO4ESM68/2025-12-31/ESEF/FR/0/529900S21EQ1BO4ESM68-2025-12-31-1-fr/reports/529900S21EQ1BO4ESM68-2025-12-31-1-fr.xhtml";
    private const string LatestBritishReport =
        "/529900S21EQ1BO4ESM68/2025-12-31/ESEF/GB/0/529900S21EQ1BO4ESM68-2025-12-31/reports/529900S21EQ1BO4ESM68-2025-12-31.xhtml";

    [Fact]
    public async Task Import_StoresTheLatestReportOfAVerifiedIssuer()
    {
        var harness = await Harness.Create(Issuer("FR"));

        await harness.Service.Import(CancellationToken.None);

        var save = harness.Saved.Should().ContainSingle().Subject;
        save.DocumentType.Should().Be(DocumentType.EsefAnnualReport);
        save.AccessionNumber.Should().Be("529900S21EQ1BO4ESM68-20251231-FR");
        save.AccessionNumber.Length.Should().Be(EsefFilingSelection.FilingReferenceLength);
        save.ReportingForDate.Should().Be(new DateOnly(2025, 12, 31));
        // The index states when it received the report, which is the only date beyond the period it gives.
        save.ReportingDate.Should().Be(new DateOnly(2026, 4, 7));
        save.SourceUrl.Should().Be("https://filings.xbrl.org" + LatestFrenchReport);
        save.Xbrl.Status.Should().Be(XbrlCaptureStatus.Captured);
        save.Xbrl.Type.Should().Be(XbrlType.InlineIxbrl);
        save.Xbrl.RawBytes.Should().Equal(harness.ReportBytes);
        // A European report has no SEC rendering of its statements, so that capture lane never queues it.
        save.ReportedStatements.Should().Be(XbrlCaptureStatus.NotPresent);
        // The retrieval body is the report's text, never its markup or its encoded payloads.
        var content = System.Text.Encoding.UTF8.GetString(save.Content);
        content.Should().Contain("| Aktywa razem |  | 693 232 | 720 184 |");
        content.Should().NotContain("base64");
    }

    [Theory]
    [InlineData("FR", LatestFrenchReport, "529900S21EQ1BO4ESM68-20251231-FR")]
    [InlineData("GB", LatestBritishReport, "529900S21EQ1BO4ESM68-20251231-GB")]
    public async Task Import_TakesTheFilingMadeInTheIssuersOwnMarketCountry(
        string marketCountry,
        string expectedReport,
        string expectedReference
    )
    {
        var harness = await Harness.Create(Issuer(marketCountry));

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().ContainSingle().Which.AccessionNumber.Should().Be(expectedReference);
        harness.Handler.Requests.Select(uri => uri.PathAndQuery).Should().Contain(expectedReport);
    }

    [Fact]
    public async Task Import_WhenTheLatestReportIsAlreadyStored_CapturesThePreviousPeriod()
    {
        var issuer = Issuer("FR");
        var harness = await Harness.Create(issuer);
        harness.Context.Add(
            new Document
            {
                EquityIssuerId = issuer.Id,
                DocumentType = DocumentType.EsefAnnualReport,
                AccessionNumber = "529900S21EQ1BO4ESM68-20251231-FR",
                ReportingDate = new DateOnly(2026, 4, 7),
                ReportingForDate = new DateOnly(2025, 12, 31),
                Content = new Equibles.Media.Data.Models.File { Name = "stored.txt" },
            }
        );
        await harness.Context.SaveChangesAsync();

        await harness.Service.Import(CancellationToken.None);

        harness
            .Saved.Should()
            .ContainSingle()
            .Which.AccessionNumber.Should()
            .Be("529900S21EQ1BO4ESM68-20241231-FR");
        harness.Handler.Requests.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Import_FutureReceiptDateCannotBecomeAFilingDate()
    {
        var harness = await Harness.Create(
            Issuer("FR"),
            rewriteIndex: index => index.Replace("2026-04-07", "2099-04-07")
        );

        await harness.Service.Import(CancellationToken.None);

        harness
            .Saved.Should()
            .ContainSingle()
            .Which.ReportingForDate.Should()
            .Be(new DateOnly(2024, 12, 31));
        harness
            .Saved.Should()
            .OnlyContain(document =>
                document.ReportingDate <= DateOnly.FromDateTime(DateTime.UtcNow)
            );
    }

    [Fact]
    public async Task Import_ReportIndexedBeforeItsPeriodEndedCannotMaskCompletedReports()
    {
        var harness = await Harness.Create(
            Issuer("FR"),
            rewriteIndex: index => index.Replace("2026-04-07", "2024-04-07")
        );

        await harness.Service.Import(CancellationToken.None);

        harness
            .Saved.Should()
            .ContainSingle()
            .Which.ReportingForDate.Should()
            .Be(new DateOnly(2024, 12, 31));
    }

    [Fact]
    public async Task Import_FutureIndexedPeriodCannotMaskCompletedAnnualReports()
    {
        var harness = await Harness.Create(
            Issuer("FR"),
            rewriteIndex: index => index.Replace("2025-12-31", "2099-12-31")
        );

        await harness.Service.Import(CancellationToken.None);

        harness
            .Saved.Should()
            .ContainSingle()
            .Which.ReportingForDate.Should()
            .Be(new DateOnly(2024, 12, 31));
        harness
            .Handler.Requests.Should()
            .NotContain(uri => uri.AbsolutePath.Contains("2099-12-31"));
    }

    [Fact]
    public async Task Import_HistoryDrainsAndThenMakesNoReportRequests()
    {
        var issuer = Issuer("FR");
        var harness = await Harness.Create(issuer);

        for (var period = 0; period < 4; period++)
        {
            await harness.Service.Import(CancellationToken.None);
            harness.Saved.Should().HaveCount(period + 1);
            var captured = harness.Saved.Last();
            harness.Context.Add(
                new Document
                {
                    EquityIssuerId = issuer.Id,
                    DocumentType = captured.DocumentType,
                    AccessionNumber = captured.AccessionNumber,
                    ReportingDate = captured.ReportingDate,
                    ReportingForDate = captured.ReportingForDate,
                    Content = new Equibles.Media.Data.Models.File
                    {
                        Name = captured.AccessionNumber + ".txt",
                    },
                }
            );
            await harness.Context.SaveChangesAsync();
        }

        harness.Handler.Requests.Clear();
        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().HaveCount(4);
        harness
            .Saved.Select(document => document.ReportingForDate)
            .Should()
            .OnlyHaveUniqueItems()
            .And.BeInDescendingOrder();
        harness.Handler.Requests.Should().OnlyContain(uri => !uri.AbsolutePath.EndsWith(".xhtml"));
    }

    [Fact]
    public async Task Import_RefusedLatestDoesNotLetHistorySetAnObsoleteFiscalCalendar()
    {
        var harness = await Harness.Create(
            Issuer("FR"),
            rewriteIndex: index => index.Replace("2024-12-31", "2024-09-30")
        );
        harness.Context.Add(
            new EsefOversizedReport
            {
                Reference = "529900S21EQ1BO4ESM68-20251231-FR",
                SourceUrl = "https://filings.xbrl.org" + LatestFrenchJson,
                HtmlSourceUrl = "https://filings.xbrl.org" + LatestFrenchReport,
                HtmlCeilingBytes = EsefReportImportService.MaxReportBytes,
                CeilingBytes = EsefReportImportService.MaxReportBytes,
                RefusedAt = DateTime.UtcNow,
            }
        );
        await harness.Context.SaveChangesAsync();

        await harness.Service.Import(CancellationToken.None);

        harness
            .Saved.Should()
            .ContainSingle()
            .Which.ReportingForDate.Should()
            .Be(new DateOnly(2024, 9, 30));
        harness.Context.ChangeTracker.Clear();
        var stored = await harness.Context.Set<EquityIssuer>().SingleAsync();
        stored.FiscalYearEndMonth.Should().Be(12);
        stored.FiscalYearEndDay.Should().Be(31);
    }

    [Fact]
    public async Task Import_AnIssuerThatAlsoFilesWithTheSec_IsLeftToThatLane()
    {
        var issuer = Issuer("FR");
        issuer.Cik = "0000320193";
        var harness = await Harness.Create(issuer);

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_RecordsTheFiscalYearEndTheReportsOwnPeriodStates()
    {
        var harness = await Harness.Create(Issuer("FR"));

        await harness.Service.Import(CancellationToken.None);

        harness.Context.ChangeTracker.Clear();
        var stored = await harness.Context.Set<EquityIssuer>().SingleAsync();
        stored.FiscalYearEndMonth.Should().Be(12);
        stored.FiscalYearEndDay.Should().Be(31);
    }

    [Fact]
    public async Task Import_LeavesARecordedFiscalYearEndAlone()
    {
        var issuer = Issuer("FR");
        issuer.FiscalYearEndMonth = 3;
        issuer.FiscalYearEndDay = 31;
        var harness = await Harness.Create(issuer);

        await harness.Service.Import(CancellationToken.None);

        harness.Context.ChangeTracker.Clear();
        var stored = await harness.Context.Set<EquityIssuer>().SingleAsync();
        stored.FiscalYearEndMonth.Should().Be(3);
        stored.FiscalYearEndDay.Should().Be(31);
    }

    [Fact]
    public async Task Import_RecordsTheFiscalYearEndEvenWhenTheDocumentCannotBeStored()
    {
        var harness = await Harness.Create(Issuer("FR"), saveThrows: true);

        await harness.Service.Import(CancellationToken.None);

        // The pass survives one issuer's failure, and the calendar the report states is already recorded,
        // so the retry that stores the document cannot label its facts from a missing calendar.
        harness.Saved.Should().BeEmpty();
        harness.Context.ChangeTracker.Clear();
        var stored = await harness.Context.Set<EquityIssuer>().SingleAsync();
        stored.FiscalYearEndMonth.Should().Be(12);
        stored.FiscalYearEndDay.Should().Be(31);
    }

    [Fact]
    public async Task Import_AReportAddressedPastTheColumnWidth_IsRefusedBeforeItIsFetched()
    {
        // The real index with one report path lengthened past Document.SourceUrl, which a save would throw
        // on and the pass would then retry every cycle for ever.
        var padding = new string('x', EsefReportImportService.MaxSourceUrlLength);
        var harness = await Harness.Create(
            Issuer("FR"),
            rewriteIndex: index =>
                index.Replace(
                    "529900S21EQ1BO4ESM68-2025-12-31-1-fr.xhtml",
                    padding + "-529900S21EQ1BO4ESM68-2025-12-31-1-fr.xhtml"
                )
        );

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().BeEmpty();
        harness.Handler.Requests.Should().ContainSingle();
    }

    // The lane stores nothing the extraction sweep would refuse to parse, so the two ceilings are one
    // number. A report past it yields no fact and the reader refuses it too.
    [Fact]
    public void TheCaptureCeilingIsTheExtractionSweepsOwnParseCeiling() =>
        ((long)EsefReportImportService.MaxReportBytes)
            .Should()
            .Be(
                Equibles
                    .Sec
                    .FinancialFacts
                    .HostedService
                    .Services
                    .XbrlFactExtractionService
                    .MaxParseableEnvelopeBytes
            );

    [Fact]
    public async Task Import_LeavesNothingTrackedWhenAnIssuersDocumentCannotBeStored()
    {
        // The save tracks its rows before it commits, and one context serves the whole pass, so a failure
        // that left them tracked would have the next issuer's save flush them outside any transaction.
        var harness = await Harness.Create(Issuer("FR"), saveThrows: true);

        await harness.Service.Import(CancellationToken.None);

        harness.Context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    public async Task Import_StopsAtTheCycleBudget(int budget, int expected)
    {
        var harness = await Harness.CreateTwo(capturesPerCycle: budget);

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().HaveCount(expected);
    }

    [Fact]
    public async Task Import_MissingLatestReportTakesPriorityOverAnotherIssuersHistory()
    {
        var harness = await Harness.CreateTwo(capturesPerCycle: 1);
        var issuer = await harness
            .Context.Set<EquityIssuer>()
            .SingleAsync(row => row.LegalEntityIdentifier == Lei);
        harness.Context.Add(
            new Document
            {
                EquityIssuerId = issuer.Id,
                DocumentType = DocumentType.EsefAnnualReport,
                AccessionNumber = "529900S21EQ1BO4ESM68-20251231-FR",
                ReportingDate = new DateOnly(2026, 4, 7),
                ReportingForDate = new DateOnly(2025, 12, 31),
                Content = new Equibles.Media.Data.Models.File { Name = "latest.txt" },
            }
        );
        await harness.Context.SaveChangesAsync();

        await harness.Service.Import(CancellationToken.None);

        harness
            .Saved.Should()
            .ContainSingle()
            .Which.AccessionNumber.Should()
            .Be("259400NFU8A8SBP6VC21-20251231-FR");
    }

    [Fact]
    public async Task Import_CapturesEachIssuerTheIndexHoldsAFilingFor()
    {
        var harness = await Harness.CreateTwo();

        await harness.Service.Import(CancellationToken.None);

        harness
            .Saved.Select(save => save.AccessionNumber)
            .Should()
            .BeEquivalentTo([
                "529900S21EQ1BO4ESM68-20251231-FR",
                "259400NFU8A8SBP6VC21-20251231-FR",
            ]);
    }

    [Fact]
    public async Task Import_ReadsEveryPageTheIndexSaysItHas()
    {
        // The real corpus is about 260 pages, so the pass must page through it rather than read the first
        // and stop. The stated count is what ends it.
        var harness = await Harness.CreateTwo(splitAcrossPages: true);

        await harness.Service.Import(CancellationToken.None);

        harness
            .Handler.Requests.Select(uri => uri.Query)
            .Should()
            .Contain(query => query.Contains("page%5Bnumber%5D=2"));
        harness.Saved.Should().HaveCount(2);
    }

    [Fact]
    public async Task Import_AnIssuerWithNoFilingInTheIndex_IsNotAFailure()
    {
        var issuer = Issuer("FR");
        issuer.LegalEntityIdentifier = "213800JQMQK3RCVSHT68";
        var harness = await Harness.Create(issuer);

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().BeEmpty();
        harness.Handler.Requests.Should().ContainSingle();
    }

    // Serves the index as one page or as one row a page, keeping the stated total, so the pass has to
    // follow the page numbers to see every row.
    private static List<string> SplitPages(string index, bool split)
    {
        if (!split)
            return [index];
        using var document = System.Text.Json.JsonDocument.Parse(index);
        var root = document.RootElement;
        var included = root.GetProperty("included").GetRawText();
        var count = root.GetProperty("data").GetArrayLength();
        return root.GetProperty("data")
            .EnumerateArray()
            .Select(row =>
                $$"""
                    {"meta":{"count":{{count}}},"data":[{{row.GetRawText()}}],"included":{{included}}}
                    """
            )
            .ToList();
    }

    private static EquityIssuer Issuer(string marketCountry) =>
        Equibles.TestSupport.EquityIssuerSeed.Create(
            Id: Guid.NewGuid(),
            Ticker: "TTE",
            Name: "TotalEnergies SE",
            LegalEntityIdentifier: Lei,
            Isin: "FR0000120271",
            MarketCountryCode: marketCountry,
            MarketIdentifierCode: marketCountry == "FR" ? "XPAR" : "XLON",
            IdentityState: EquityIdentityState.Verified,
            TradingCurrency: "EUR",
            QuoteUnitMultiplier: 1m
        );

    [Fact]
    public async Task Import_AReportPastTheCeiling_IsRememberedSoItsBytesAreNotFetchedAgain()
    {
        var harness = await Harness.Create(Issuer("FR"));
        harness.Handler.OverstatedContentLength = 200_000_000;
        harness.Handler.OverstatedPaths.Add(LatestFrenchReport);

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().BeEmpty();
        harness.Context.ChangeTracker.Clear();
        var refusal = await harness.Context.Set<EsefOversizedReport>().SingleAsync();
        refusal.Reference.Should().Be("529900S21EQ1BO4ESM68-20251231-FR");
        refusal.CeilingBytes.Should().Be(EsefReportImportService.MaxReportBytes);
        refusal.SourceUrl.Should().Be("https://filings.xbrl.org" + LatestFrenchReport);

        harness.Handler.Requests.Clear();
        await harness.Service.Import(CancellationToken.None);

        // The index is read again, because a later period would still be captured. The report's own
        // address is not: the host states no length, so reaching that refusal again costs the download
        // again, which is the whole reason the row exists.
        harness.Handler.Requests.Should().NotBeEmpty();
        harness
            .Handler.Requests.Select(uri => uri.AbsolutePath)
            .Should()
            .NotContain(LatestFrenchReport);
    }

    [Fact]
    public async Task Import_ARefusalSpendsTheCycleBudget()
    {
        // A refusal costs the whole download here, so it has to be charged; it is safe to charge only
        // because it is remembered, so one report spends the budget once rather than every cycle.
        var harness = await Harness.CreateTwo(capturesPerCycle: 1);
        harness.Handler.OverstatedContentLength = 200_000_000;
        harness.Handler.OverstatedPaths.Add(LatestFrenchReport);
        harness.Handler.OverstatedPaths.Add(LatestFrenchReport.Replace(Lei, SecondLei));

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().BeEmpty();
        harness
            .Handler.Requests.Where(uri => uri.AbsolutePath.EndsWith(".xhtml"))
            .Should()
            .ContainSingle();
    }

    [Fact]
    public async Task Import_AReportStillPastARaisedCeiling_HasItsRefusalRestamped()
    {
        // The only path that runs when the ceiling rises and the report is still too large. Left unwritten,
        // the row keeps its old ceiling, never qualifies again, and the report is re-fetched every cycle,
        // which is the defect this change exists to close.
        var harness = await Harness.Create(Issuer("FR"));
        harness.Context.Add(
            new EsefOversizedReport
            {
                Reference = "529900S21EQ1BO4ESM68-20251231-FR",
                SourceUrl = "https://filings.xbrl.org" + LatestFrenchReport,
                CeilingBytes = EsefReportImportService.MaxReportBytes - 1,
                RefusedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            }
        );
        await harness.Context.SaveChangesAsync();
        harness.Handler.OverstatedContentLength = 200_000_000;
        harness.Handler.OverstatedPaths.Add(LatestFrenchReport);

        await harness.Service.Import(CancellationToken.None);

        harness.Context.ChangeTracker.Clear();
        var refusal = await harness.Context.Set<EsefOversizedReport>().SingleAsync();
        refusal.CeilingBytes.Should().Be(EsefReportImportService.MaxReportBytes);

        harness.Handler.Requests.Clear();
        await harness.Service.Import(CancellationToken.None);

        harness
            .Handler.Requests.Select(uri => uri.AbsolutePath)
            .Should()
            .NotContain(LatestFrenchReport);
    }

    [Fact]
    public async Task Import_AFilingAlreadyRefused_DoesNotSpendTheCycleBudget()
    {
        // Skipping a remembered refusal costs no request, so charging it would cut real captures by one
        // for every oversized report the ledger holds.
        var harness = await Harness.CreateTwo(capturesPerCycle: 1);
        harness.Context.Add(
            new EsefOversizedReport
            {
                Reference = "529900S21EQ1BO4ESM68-20251231-FR",
                SourceUrl = "https://filings.xbrl.org" + LatestFrenchJson,
                HtmlSourceUrl = "https://filings.xbrl.org" + LatestFrenchReport,
                HtmlCeilingBytes = EsefReportImportService.MaxReportBytes,
                CeilingBytes = EsefReportImportService.MaxReportBytes,
                RefusedAt = DateTime.UtcNow,
            }
        );
        await harness.Context.SaveChangesAsync();

        await harness.Service.Import(CancellationToken.None);

        harness
            .Saved.Should()
            .ContainSingle()
            .Which.AccessionNumber.Should()
            .Be("259400NFU8A8SBP6VC21-20251231-FR");
    }

    [Fact]
    public async Task Import_AReportStoredUnderARaisedCeiling_LeavesNoRefusalBehind()
    {
        var harness = await Harness.Create(Issuer("FR"));
        harness.Context.Add(
            new EsefOversizedReport
            {
                Reference = "529900S21EQ1BO4ESM68-20251231-FR",
                SourceUrl = "https://filings.xbrl.org" + LatestFrenchReport,
                CeilingBytes = EsefReportImportService.MaxReportBytes - 1,
                RefusedAt = DateTime.UtcNow,
            }
        );
        await harness.Context.SaveChangesAsync();

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().ContainSingle();
        harness.Context.ChangeTracker.Clear();
        // A row saying the report was refused, beside the document that holds it, is evidence of nothing.
        (await harness.Context.Set<EsefOversizedReport>().CountAsync())
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task Import_AReportRefusedUnderALowerCeiling_IsTriedAgain()
    {
        // The row records the ceiling the report exceeded, never its size, which this host never states.
        // Raising the ceiling therefore has to re-open every filing refused under a lower one.
        var harness = await Harness.Create(Issuer("FR"));
        harness.Context.Add(
            new EsefOversizedReport
            {
                Reference = "529900S21EQ1BO4ESM68-20251231-FR",
                SourceUrl = "https://filings.xbrl.org" + LatestFrenchReport,
                CeilingBytes = EsefReportImportService.MaxReportBytes - 1,
                RefusedAt = DateTime.UtcNow,
            }
        );
        await harness.Context.SaveChangesAsync();

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().ContainSingle();
    }

    private sealed record SavedDocument(
        Guid IssuerId,
        byte[] Content,
        DocumentType DocumentType,
        DateOnly ReportingDate,
        DateOnly ReportingForDate,
        string SourceUrl,
        string AccessionNumber,
        XbrlCaptureResult Xbrl,
        XbrlCaptureStatus ReportedStatements
    );

    [Fact]
    public async Task Import_OversizedHtml_RecoversSourceJsonOnTheNextCycleWithoutRefetchingHtml()
    {
        var json = JsonReport();
        var harness = await Harness.Create(Issuer("FR"), jsonReport: json);
        harness.Handler.OverstatedContentLength = 200_000_000;
        harness.Handler.OverstatedPaths.Add(LatestFrenchReport);
        await harness.Service.Import(CancellationToken.None);
        harness.Saved.Should().BeEmpty();
        harness.Handler.Requests.Clear();

        await harness.Service.Import(CancellationToken.None);

        var saved = harness.Saved.Should().ContainSingle().Subject;
        saved.Xbrl.Type.Should().Be(XbrlType.JsonXbrl);
        saved.Xbrl.RawBytes.Should().Equal(System.Text.Encoding.UTF8.GetBytes(json));
        saved.SourceUrl.Should().Be("https://filings.xbrl.org" + LatestFrenchJson);
        saved.Content.Should().BeEmpty();
        saved.AccessionNumber.Should().Be("529900S21EQ1BO4ESM68-20251231-FR");
        harness
            .Handler.Requests.Select(uri => uri.AbsolutePath)
            .Should()
            .Contain(LatestFrenchJson)
            .And.NotContain(LatestFrenchReport);
        (await harness.Context.Set<EsefOversizedReport>().CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("wrong-issuer")]
    [InlineData("wrong-period")]
    [InlineData("comparative-only-period")]
    [InlineData("conflicting-current-facts")]
    public async Task Import_UnusableJson_PreservesRetryAndOriginalRefusal(string failure)
    {
        var json =
            failure == "malformed"
                ? "<html>unavailable</html>"
                : JsonReport().Replace(Lei, SecondLei);
        if (failure == "wrong-period")
            json = JsonReport().Replace("2026-01-01T00:00:00", "2024-01-01T00:00:00");
        if (failure == "comparative-only-period")
            json = JsonReport().Replace("2025-01-01T00:00:00", "2027-01-01T00:00:00");
        if (failure == "conflicting-current-facts")
            json = JsonReport().Replace("2025-01-01T00:00:00", "2026-01-01T00:00:00");
        var harness = await Harness.Create(Issuer("FR"), jsonReport: json);
        await SeedHtmlRefusal(harness);

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().BeEmpty();
        (await harness.Context.Set<EsefOversizedReport>().SingleAsync())
            .SourceUrl.Should()
            .EndWith(LatestFrenchReport);
        harness
            .Handler.Requests.Select(uri => uri.AbsolutePath)
            .Should()
            .Contain(LatestFrenchJson)
            .And.NotContain(LatestFrenchReport);
    }

    [Fact]
    public async Task Import_OversizedJson_RemembersTheRepresentationAndDoesNotFetchEitherAgain()
    {
        var harness = await Harness.Create(Issuer("FR"), jsonReport: JsonReport());
        await SeedHtmlRefusal(harness);
        harness.Handler.OverstatedContentLength = 200_000_000;
        harness.Handler.OverstatedPaths.Add(LatestFrenchJson);

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().BeEmpty();
        (await harness.Context.Set<EsefOversizedReport>().SingleAsync())
            .SourceUrl.Should()
            .EndWith(LatestFrenchJson);
        harness.Handler.Requests.Clear();
        await harness.Service.Import(CancellationToken.None);
        harness
            .Handler.Requests.Select(uri => uri.AbsolutePath)
            .Should()
            .NotContain(LatestFrenchJson)
            .And.NotContain(LatestFrenchReport);
    }

    [Fact]
    public async Task Import_ChangedJsonAddress_DoesNotForgetTheUnchangedHtmlRefusal()
    {
        var changedJson = LatestFrenchJson.Replace("-1-fr.json", "-2-fr.json");
        var harness = await Harness.Create(
            Issuer("FR"),
            rewriteIndex: index => index.Replace(LatestFrenchJson, changedJson),
            jsonReport: JsonReport()
        );
        harness.Context.Add(
            new EsefOversizedReport
            {
                Reference = "529900S21EQ1BO4ESM68-20251231-FR",
                SourceUrl = "https://filings.xbrl.org" + LatestFrenchJson,
                CeilingBytes = EsefReportImportService.MaxReportBytes,
                HtmlSourceUrl = "https://filings.xbrl.org" + LatestFrenchReport,
                HtmlCeilingBytes = EsefReportImportService.MaxReportBytes,
                RefusedAt = DateTime.UtcNow,
            }
        );
        await harness.Context.SaveChangesAsync();

        await harness.Service.Import(CancellationToken.None);

        harness.Saved.Should().ContainSingle().Which.SourceUrl.Should().EndWith(changedJson);
        harness
            .Handler.Requests.Select(uri => uri.AbsolutePath)
            .Should()
            .Contain(changedJson)
            .And.NotContain(LatestFrenchReport)
            .And.NotContain(LatestFrenchJson);
    }

    [Fact]
    public async Task Import_OversizedReplacementHtml_PreservesTheUnchangedJsonRefusal()
    {
        var changedHtml = LatestFrenchReport.Replace("-1-fr.xhtml", "-2-fr.xhtml");
        var harness = await Harness.Create(
            Issuer("FR"),
            rewriteIndex: index => index.Replace(LatestFrenchReport, changedHtml),
            jsonReport: JsonReport()
        );
        harness.Context.Add(
            new EsefOversizedReport
            {
                Reference = "529900S21EQ1BO4ESM68-20251231-FR",
                SourceUrl = "https://filings.xbrl.org" + LatestFrenchJson,
                CeilingBytes = EsefReportImportService.MaxReportBytes,
                HtmlSourceUrl = "https://filings.xbrl.org" + LatestFrenchReport,
                HtmlCeilingBytes = EsefReportImportService.MaxReportBytes,
                RefusedAt = DateTime.UtcNow,
            }
        );
        await harness.Context.SaveChangesAsync();
        harness.Handler.OverstatedContentLength = 200_000_000;
        harness.Handler.OverstatedPaths.Add(changedHtml);

        await harness.Service.Import(CancellationToken.None);

        var refusal = await harness.Context.Set<EsefOversizedReport>().SingleAsync();
        refusal.SourceUrl.Should().EndWith(LatestFrenchJson);
        refusal.HtmlSourceUrl.Should().EndWith(changedHtml);
        harness.Handler.Requests.Clear();
        await harness.Service.Import(CancellationToken.None);
        harness
            .Handler.Requests.Select(uri => uri.AbsolutePath)
            .Should()
            .NotContain(LatestFrenchJson)
            .And.NotContain(changedHtml)
            .And.NotContain(LatestFrenchReport);
    }

    private static string JsonReport() =>
        File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "TestAssets",
                    "Esef",
                    "ctt-2022-json-excerpt.json"
                )
            )
            .Replace("529900G4A1IKOKC22K56", Lei)
            .Replace("2022-01-01T00:00:00", "2025-01-01T00:00:00")
            .Replace("2023-01-01T00:00:00", "2026-01-01T00:00:00");

    private static async Task SeedHtmlRefusal(Harness harness)
    {
        harness.Context.Add(
            new EsefOversizedReport
            {
                Reference = "529900S21EQ1BO4ESM68-20251231-FR",
                SourceUrl = "https://filings.xbrl.org" + LatestFrenchReport,
                CeilingBytes = EsefReportImportService.MaxReportBytes,
                RefusedAt = DateTime.UtcNow,
            }
        );
        await harness.Context.SaveChangesAsync();
    }

    private sealed class Harness
    {
        public EquiblesFinancialDbContext Context { get; private init; }
        public EsefReportImportService Service { get; private set; }
        public EsefIndexTestHandler Handler { get; private init; }
        public List<SavedDocument> Saved { get; } = [];
        public byte[] ReportBytes { get; private init; }

        // Two issuers over the derived two-issuer index; see TestAssets/Esef/README.md.
        public static Task<Harness> CreateTwo(
            int capturesPerCycle = 100,
            bool splitAcrossPages = false
        ) =>
            Create(
                Issuer("FR"),
                capturesPerCycle,
                second: Equibles.TestSupport.EquityIssuerSeed.Create(
                    Id: Guid.NewGuid(),
                    Ticker: "IZS",
                    Name: "Izostal S.A.",
                    LegalEntityIdentifier: "259400NFU8A8SBP6VC21",
                    Isin: "PLIZSTL00013",
                    MarketCountryCode: "FR",
                    MarketIdentifierCode: "XPAR",
                    IdentityState: EquityIdentityState.Verified,
                    TradingCurrency: "EUR",
                    QuoteUnitMultiplier: 1m
                ),
                splitAcrossPages: splitAcrossPages
            );

        public static async Task<Harness> Create(
            EquityIssuer issuer,
            int capturesPerCycle = 100,
            bool saveThrows = false,
            Func<string, string> rewriteIndex = null,
            EquityIssuer second = null,
            bool splitAcrossPages = false,
            string jsonReport = null
        )
        {
            var context = NewDb();
            context.Add(issuer);
            if (second != null)
                context.Add(second);
            await context.SaveChangesAsync();

            var index = File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "TestAssets",
                    "Esef",
                    second == null ? "filings-one-issuer.json" : "filings-two-issuers.json"
                )
            );
            if (rewriteIndex != null)
                index = rewriteIndex(index);
            var report = File.ReadAllBytes(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "TestAssets",
                    "Esef",
                    "izs-2022-excerpt.xhtml"
                )
            );
            var reportText = System.Text.Encoding.UTF8.GetString(report);
            var pages = SplitPages(index, splitAcrossPages);
            var bodies = new Dictionary<string, string>
            {
                [LatestFrenchReport] = reportText,
                [LatestBritishReport] = reportText,
                [LatestFrenchReport.Replace(Lei, SecondLei)] = reportText,
                [LatestBritishReport.Replace(Lei, SecondLei)] = reportText,
            };
            foreach (
                var filing in XbrlFilingsParser
                    .Read(index, new Uri("https://filings.xbrl.org"))
                    .Filings
            )
            {
                bodies[filing.ReportUrl.PathAndQuery] = reportText;
                if (jsonReport != null && filing.JsonUrl != null)
                    bodies[filing.JsonUrl.PathAndQuery] = jsonReport;
            }
            for (var page = 1; page <= pages.Count; page++)
            {
                bodies[
                    XbrlFilingsClient
                        .IndexUrl(page, EsefReportImportService.IndexPageSize)
                        .PathAndQuery
                ] = pages[page - 1];
            }
            var handler = new EsefIndexTestHandler(bodies);
            var client = new XbrlFilingsClient(new HttpClient(handler))
            {
                Pace = new Equibles.Integrations.Common.RateLimiter.RateLimiter(
                    1000,
                    TimeSpan.FromSeconds(1)
                ),
            };

            var harness = new Harness
            {
                Context = context,
                Handler = handler,
                ReportBytes = report,
            };
            var persistence = Substitute.For<IDocumentPersistenceService>();
            persistence
                .Save(
                    Arg.Any<EquityIssuer>(),
                    Arg.Any<byte[]>(),
                    Arg.Any<string>(),
                    Arg.Any<DocumentType>(),
                    Arg.Any<DateOnly>(),
                    Arg.Any<DateOnly>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<XbrlCaptureResult>(),
                    Arg.Any<AsFiledHtmlCaptureResult>(),
                    Arg.Any<XbrlCaptureStatus>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(call =>
                {
                    if (saveThrows)
                    {
                        // What the real save does before it commits, so a missing cleanup is visible.
                        harness.Context.Add(
                            new Equibles.Media.Data.Models.File { Name = "half-written.txt" }
                        );
                        throw new InvalidOperationException("the document could not be stored");
                    }
                    harness.Saved.Add(
                        new SavedDocument(
                            call.Arg<EquityIssuer>().Id,
                            call.ArgAt<byte[]>(1),
                            call.ArgAt<DocumentType>(3),
                            call.ArgAt<DateOnly>(4),
                            call.ArgAt<DateOnly>(5),
                            call.ArgAt<string>(6),
                            call.ArgAt<string>(7),
                            call.ArgAt<XbrlCaptureResult>(9),
                            call.ArgAt<XbrlCaptureStatus>(11)
                        )
                    );
                    return Task.CompletedTask;
                });

            var issuers = new EquityIssuerRepository(context);
            harness.Service = new EsefReportImportService(
                client,
                issuers,
                new DocumentRepository(context),
                new EsefOversizedReportRepository(context),
                persistence,
                new EquityIdentityManager(issuers, Substitute.For<IBus>()),
                new SecDocumentHtmlNormalizer(),
                new SecDocumentHtmlToMarkdownConverter(),
                Options.Create(
                    new EsefReportScraperOptions { MaxCapturesPerCycle = capturesPerCycle }
                ),
                NullLogger<EsefReportImportService>.Instance
            );
            return harness;
        }
    }

    // The Sec module also maps the chunk embeddings, whose pgvector type the in-memory provider cannot
    // construct. This lane writes documents and reads them back, so only that half is registered.
    private sealed class DocumentsOnlySecModule : IModuleConfiguration
    {
        public void ConfigureEntities(ModelBuilder builder)
        {
            builder.Entity<Document>(entity =>
            {
                entity
                    .Property(document => document.DocumentType)
                    .HasConversion(
                        new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<
                            DocumentType,
                            string
                        >(value => value.Value, value => DocumentType.FromValue(value))
                    );
                entity.Ignore(document => document.Chunks);
                entity.Ignore(document => document.Images);
                entity.Ignore(document => document.Artifacts);
            });
            builder.Entity<EsefOversizedReport>();
            builder.Ignore<Equibles.Sec.Data.Models.Chunks.Chunk>();
            builder.Ignore<Equibles.Sec.Data.Models.Chunks.Embedding>();
        }
    }

    private static EquiblesFinancialDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<EquiblesFinancialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = new EquiblesFinancialDbContext(
            options,
            new IModuleConfiguration[]
            {
                new CommonStocksModuleConfiguration(),
                new MediaModuleConfiguration(),
                new DocumentsOnlySecModule(),
            }
        );
        context.Database.EnsureCreated();
        return context;
    }
}
