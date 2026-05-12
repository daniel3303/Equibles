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
