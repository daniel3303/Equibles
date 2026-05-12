using System.Net;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

public class SecEdgarClientTests {
    [Fact]
    public async Task GetActiveCompanies_MultipleTickerRowsForSameCik_CollapsesIntoOneCompanyWithPrimaryTickerFirst() {
        // The SEC company_tickers_exchange.json emits one ROW per (cik, ticker), so a company
        // with multiple share classes (e.g. Alphabet GOOG + GOOGL) appears twice. The parser
        // must group by CIK and keep the FIRST ticker as the primary — a regression that
        // either creates duplicate companies, drops the secondary ticker, or reorders the
        // list would silently break downstream filings ingestion.
        var json = """
            {
              "fields": ["cik", "name", "ticker", "exchange"],
              "data": [
                [1652044, "Alphabet Inc.", "GOOGL", "Nasdaq"],
                [1652044, "Alphabet Inc.", "GOOG", "Nasdaq"]
              ]
            }
            """;

        var handler = new ScriptedHandler(json);
        var httpClient = new HttpClient(handler);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" })
            .Build();
        var sut = new SecEdgarClient(httpClient, Substitute.For<ILogger<SecEdgarClient>>(), config);

        var companies = await sut.GetActiveCompanies();

        companies.Should().ContainSingle();
        companies[0].Cik.Should().Be("1652044");
        companies[0].Name.Should().Be("Alphabet Inc.");
        companies[0].Tickers.Should().Equal("GOOGL", "GOOG");
    }

    [Fact]
    public async Task GetCompanyFilings_DocumentTypeFilterFormFour_KeepsOnlyForm4FilingsFromRecentList() {
        // The SEC `submissions/CIK{n}.json` payload returns filings as parallel column-arrays
        // (`form`, `accessionNumber`, `filingDate`, …) mixing every form type the company has
        // ever filed. Insider-trading ingestion only wants Form 4 filings out of that bag.
        // GetCompanyFilings runs the SEC payload through MapToFilingData → FilterFilings,
        // where the form filter compares against `DocumentTypeFilter.FormFour.GetFormName()` —
        // which goes through `[Display(Name = "4")]` reflection on the enum value. A
        // regression in any of those three layers (column zip, reflection-based form-name
        // lookup, equality check) would either drop the Form 4 row or smuggle in the 10-K /
        // 13F-HR neighbours. This `[Fact]` pins exactly that path: three mixed-type filings
        // in `recent`, one of them Form 4, only the Form 4 survives the filter — and the
        // returned `FilingData.Form` is the SEC string `"4"`, not `"FormFour"` (the C#
        // enum name) — distinguishing the display-name leg from the value leg.
        var json = """
            {
              "cik": "1234567",
              "name": "Test Co",
              "filings": {
                "recent": {
                  "accessionNumber": ["0001-24-000001", "0001-24-000002", "0001-24-000003"],
                  "filingDate":      ["2024-03-15",     "2024-03-20",     "2024-03-25"],
                  "reportDate":      ["2024-03-14",     "2023-12-31",     "2023-12-31"],
                  "form":            ["4",              "10-K",           "13F-HR"],
                  "primaryDocument": ["wf-form4.xml",   "tenk.htm",       "13fhr.xml"],
                  "primaryDocDescription": ["",         "",               ""]
                },
                "files": []
              }
            }
            """;

        var handler = new ScriptedHandler(json);
        var httpClient = new HttpClient(handler);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" })
            .Build();
        var sut = new SecEdgarClient(httpClient, Substitute.For<ILogger<SecEdgarClient>>(), config);

        var filings = await sut.GetCompanyFilings("1234567", documentType: DocumentTypeFilter.FormFour);

        filings.Should().ContainSingle();
        filings[0].Form.Should().Be("4");
        filings[0].AccessionNumber.Should().Be("0001-24-000001");
    }

    [Fact]
    public async Task GetCompanyFilings_FromDateAfterArchiveRange_SkipsArchiveFetchEntirely() {
        // SEC paginates older filings into separate JSON files listed in `filings.files`, each
        // tagged with `filingFrom`/`filingTo`. `GetArchiveFilings` looks at the requested
        // date window and skips archives whose entire range falls outside it — saving an
        // expensive HTTP fetch (each archive is ~hundreds of filings, and the SEC rate-limit
        // is unforgiving). A regression here either drops data the caller asked for (skipping
        // an in-range archive) or hammers SEC for nothing (fetching an archive that can't
        // contribute).
        //
        // This `[Fact]` ships a `recent` list with zero filings plus ONE archive whose
        // `filingTo` (2020-12-31) is before the requested `fromDate` (2024-01-01). The
        // ScriptedHandler holds exactly ONE response — the main submissions JSON. If the
        // production code wrongly fetches the archive, `ScriptedHandler.SendAsync` throws
        // "ScriptedHandler exhausted", the test surfaces that exception, and the failure
        // points squarely at the missing skip. Asserting the resulting list is empty also
        // protects against a regression that pulled archive entries out of thin air.
        var json = """
            {
              "cik": "1234567",
              "name": "Test Co",
              "filings": {
                "recent": {
                  "accessionNumber": [],
                  "filingDate": [],
                  "reportDate": [],
                  "form": [],
                  "primaryDocument": [],
                  "primaryDocDescription": []
                },
                "files": [
                  {
                    "name": "CIK0001234567-submissions-001.json",
                    "filingCount": 100,
                    "filingFrom": "2020-01-01",
                    "filingTo": "2020-12-31"
                  }
                ]
              }
            }
            """;

        var handler = new ScriptedHandler(json);
        var httpClient = new HttpClient(handler);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" })
            .Build();
        var sut = new SecEdgarClient(httpClient, Substitute.For<ILogger<SecEdgarClient>>(), config);

        var filings = await sut.GetCompanyFilings("1234567", fromDate: new DateOnly(2024, 1, 1));

        filings.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCompanyFilings_RecentEmptyButArchiveInRange_FetchesArchiveAndMergesItsFilings() {
        // Complement to GetCompanyFilings_FromDateAfterArchiveRange_SkipsArchiveFetchEntirely:
        // there the test pinned that an out-of-window archive is NOT fetched. Here we pin the
        // opposite leg — when the archive overlaps the request window, the second HTTP fetch
        // happens, the archive JSON deserialises through `RecentFilings` (same shape as the
        // main "recent" block), gets `MapToFilingData`'d, and is merged into the returned
        // list via `AddRange` + `DistinctBy(AccessionNumber)`.
        //
        // A regression here can manifest as: archive contents silently dropped (caller sees
        // empty results despite filings existing on EDGAR), or — once both legs are wired —
        // a duplicate row when an accession appears in both `recent` and the archive but the
        // DistinctBy is removed. This `[Fact]` ships an empty `recent` so any returned
        // filing must have come from the archive fetch path, then asserts the archive's one
        // Form 4 row is what we get back (AccessionNumber, Form).
        var mainJson = """
            {
              "cik": "1234567",
              "name": "Test Co",
              "filings": {
                "recent": {
                  "accessionNumber": [],
                  "filingDate": [],
                  "reportDate": [],
                  "form": [],
                  "primaryDocument": [],
                  "primaryDocDescription": []
                },
                "files": [
                  {
                    "name": "CIK0001234567-submissions-001.json",
                    "filingCount": 1,
                    "filingFrom": "2020-01-01",
                    "filingTo": "2020-12-31"
                  }
                ]
              }
            }
            """;
        var archiveJson = """
            {
              "accessionNumber": ["0001-20-000099"],
              "filingDate":      ["2020-06-15"],
              "reportDate":      ["2020-06-14"],
              "form":            ["4"],
              "primaryDocument": ["wf-form4.xml"],
              "primaryDocDescription": [""]
            }
            """;

        var handler = new ScriptedHandler(mainJson, archiveJson);
        var httpClient = new HttpClient(handler);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" })
            .Build();
        var sut = new SecEdgarClient(httpClient, Substitute.For<ILogger<SecEdgarClient>>(), config);

        var filings = await sut.GetCompanyFilings("1234567");

        filings.Should().ContainSingle();
        filings[0].AccessionNumber.Should().Be("0001-20-000099");
        filings[0].Form.Should().Be("4");
    }

    [Fact]
    public async Task GetCompanyMetadata_OperatingCompanyOnNasdaq_LiftsEntityTypeAndExchangesFromJson() {
        // GetCompanyMetadata is the entry point CompanySyncService leans on to decide if a
        // CIK represents a real operating company or a non-issuer (subsidiary that files
        // but isn't separately listed). The decision flows through CompanyMetadata's two
        // derived flags: `IsOperatingCompany` reads `EntityType`, `IsListed` reads
        // `Exchanges`. Both originate in the SEC submissions JSON. A regression in the
        // JSON-property mapping — e.g. someone reading `entityType` from the wrong field
        // after a Newtonsoft rename, or dropping the `?? []` Exchanges defensive — would
        // silently misclassify every company sync from that point on.
        //
        // This `[Fact]` ships the smallest representative SEC payload: an operating
        // company listed on Nasdaq with a single-entry `exchanges` array. Asserts each of
        // the three properties `GetCompanyMetadata` populates — `Cik` (echoed from the
        // input parameter, not from the body), `EntityType` (from `entityType`),
        // `Exchanges` (from `exchanges`) — so a swapped or renamed field surfaces here
        // rather than as a subtle classification drift downstream.
        var json = """
            {
              "cik": "1234567",
              "name": "Test Co",
              "entityType": "operating",
              "exchanges": ["Nasdaq"],
              "filings": { "recent": { }, "files": [] }
            }
            """;

        var handler = new ScriptedHandler(json);
        var httpClient = new HttpClient(handler);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" })
            .Build();
        var sut = new SecEdgarClient(httpClient, Substitute.For<ILogger<SecEdgarClient>>(), config);

        var metadata = await sut.GetCompanyMetadata("1234567");

        metadata.Should().NotBeNull();
        metadata!.Cik.Should().Be("1234567");
        metadata.EntityType.Should().Be("operating");
        metadata.Exchanges.Should().Equal("Nasdaq");
    }

    [Fact]
    public async Task GetCompanyFilings_ToDateInsideRecentRange_DropsFilingsStrictlyAfterIt() {
        // FilterFilings applies the upper-bound `toDate` clause `f.FilingDate <= toDate`
        // to every row that survives the earlier mapping. This is structurally different
        // from the archive-skip path (PR #108): the archive skip avoids the HTTP fetch
        // entirely; the toDate filter operates on per-row `FilingData` after MapToFilingData.
        // A regression that flipped the inequality (`< toDate` or `> toDate`) would drop
        // filings filed exactly on the boundary, or admit filings strictly after it.
        //
        // This `[Fact]` ships two `recent` filings — January and December of the same year —
        // and sets `toDate = 2024-06-30`. Only the January row should survive (Jan < Jun).
        // Asserts the December filing is gone (proves the upper bound bites) and that
        // exactly one filing returns with the January accession (proves the comparison is
        // per-row, not all-or-nothing).
        var json = """
            {
              "cik": "1234567",
              "name": "Test Co",
              "filings": {
                "recent": {
                  "accessionNumber": ["0001-24-JAN", "0001-24-DEC"],
                  "filingDate":      ["2024-01-15", "2024-12-15"],
                  "reportDate":      ["2024-01-14", "2024-12-14"],
                  "form":            ["4",          "4"],
                  "primaryDocument": ["jan.xml",    "dec.xml"],
                  "primaryDocDescription": ["",     ""]
                },
                "files": []
              }
            }
            """;

        var handler = new ScriptedHandler(json);
        var httpClient = new HttpClient(handler);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" })
            .Build();
        var sut = new SecEdgarClient(httpClient, Substitute.For<ILogger<SecEdgarClient>>(), config);

        var filings = await sut.GetCompanyFilings("1234567", toDate: new DateOnly(2024, 6, 30));

        filings.Should().ContainSingle();
        filings[0].AccessionNumber.Should().Be("0001-24-JAN");
    }

    [Fact]
    public async Task GetCompanyFilings_SameAccessionInRecentAndArchive_DistinctByCollapsesToOneRow() {
        // SEC paginates older filings into archive files but the boundaries occasionally
        // overlap — the same `accessionNumber` can appear in both `filings.recent` and a
        // listed archive. `GetCompanyFilings` runs `.DistinctBy(f => f.AccessionNumber)`
        // before applying the per-row filters; without it, the same Form 4 would be
        // inserted twice into the result list and (downstream) cause the unique-index
        // violation in `InsiderTransactionRepository` on the next save.
        //
        // This `[Fact]` ships TWO responses: the main `recent` block carrying one Form 4
        // row, plus an archive whose filingFrom/filingTo overlaps the recent date and
        // which carries the SAME accession (different primaryDocument string to make
        // sure the test is comparing accessions, not whole rows). Asserts the result
        // collapses to exactly one filing.
        var mainJson = """
            {
              "cik": "1234567",
              "name": "Test Co",
              "filings": {
                "recent": {
                  "accessionNumber": ["0001-20-DUP"],
                  "filingDate":      ["2020-06-15"],
                  "reportDate":      ["2020-06-14"],
                  "form":            ["4"],
                  "primaryDocument": ["recent-form4.xml"],
                  "primaryDocDescription": [""]
                },
                "files": [
                  {
                    "name": "CIK0001234567-submissions-001.json",
                    "filingCount": 1,
                    "filingFrom": "2020-01-01",
                    "filingTo": "2020-12-31"
                  }
                ]
              }
            }
            """;
        var archiveJson = """
            {
              "accessionNumber": ["0001-20-DUP"],
              "filingDate":      ["2020-06-15"],
              "reportDate":      ["2020-06-14"],
              "form":            ["4"],
              "primaryDocument": ["archive-form4.xml"],
              "primaryDocDescription": [""]
            }
            """;

        var handler = new ScriptedHandler(mainJson, archiveJson);
        var httpClient = new HttpClient(handler);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { ["Sec:ContactEmail"] = "test@example.com" })
            .Build();
        var sut = new SecEdgarClient(httpClient, Substitute.For<ILogger<SecEdgarClient>>(), config);

        var filings = await sut.GetCompanyFilings("1234567");

        filings.Should().ContainSingle();
        filings[0].AccessionNumber.Should().Be("0001-20-DUP");
    }

    private sealed class ScriptedHandler : HttpMessageHandler {
        private readonly Queue<string> _responses;

        public ScriptedHandler(params string[] responses) {
            _responses = new Queue<string>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            if (_responses.Count == 0) {
                throw new InvalidOperationException("ScriptedHandler exhausted");
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {
                Content = new StringContent(_responses.Dequeue()),
            });
        }
    }
}
