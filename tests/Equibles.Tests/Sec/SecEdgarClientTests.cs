using System.Net;
using Equibles.Integrations.Sec;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.Tests.Sec;

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
