using System.Net;
using Equibles.CommonStocks.Data.Models;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models.Responses;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Equibles.UnitTests.Sec;

// The multi-CIK facts contract (GH-7041): one import reads the primary plus every
// attached secondary CIK, and a fetch failure on ANY of them must abort the cycle
// before a single write — otherwise the checkpoint advances past an unread source
// and a predecessor's older facts are skipped forever.
public class FinancialFactsImportServiceMultiCikTests
{
    [Fact]
    public void CiksFor_PrimaryFirstThenSecondaries()
    {
        var stock = new CommonStock
        {
            Ticker = "XOM",
            Name = "Exxon Mobil",
            Cik = "2115436",
            SecondaryCiks = ["34088", "99999"],
        };

        FinancialFactsImportService.CiksFor(stock).Should().Equal("2115436", "34088", "99999");
    }

    [Fact]
    public void CiksFor_DuplicateSecondary_IsReadOnce()
    {
        // The company sync's subsidiary attach writes SEC's value verbatim, so a
        // duplicate can exist in the column; reading it twice would double the
        // companyfacts download and the in-memory parsed set.
        var stock = new CommonStock
        {
            Ticker = "XOM",
            Name = "Exxon Mobil",
            Cik = "2115436",
            SecondaryCiks = ["34088", "34088", "2115436"],
        };

        FinancialFactsImportService.CiksFor(stock).Should().Equal("2115436", "34088");
    }

    [Fact]
    public void CiksFor_NoSecondaries_IsJustThePrimary()
    {
        var stock = new CommonStock
        {
            Ticker = "AAPL",
            Name = "Apple",
            Cik = "320193",
            SecondaryCiks = [],
        };

        FinancialFactsImportService.CiksFor(stock).Should().Equal("320193");
    }

    [Fact]
    public async Task Import_SecondaryCikFetchFails_WritesNothing()
    {
        // Primary answers (no data), the attached CIK's download fails: the whole
        // cycle must abort BEFORE any repository scope is opened. If a refactor
        // turns the failure `return` into a `continue`, the import persists a
        // partial union and advances the checkpoint — this pins the early return.
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var secEdgarClient = Substitute.For<ISecEdgarClient>();
        secEdgarClient.GetCompanyFacts("2115436").Returns((CompanyFactsResponse)null);
        secEdgarClient
            .GetCompanyFacts("34088")
            .ThrowsAsync(new HttpRequestException("boom", null, HttpStatusCode.ServiceUnavailable));

        var sut = new FinancialFactsImportService(
            scopeFactory,
            secEdgarClient,
            Substitute.For<ILogger<FinancialFactsImportService>>(),
            new ErrorReporter(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );
        var stock = new CommonStock
        {
            Ticker = "XOM",
            Name = "Exxon Mobil",
            Cik = "2115436",
            SecondaryCiks = ["34088"],
        };

        await sut.Import(stock, CancellationToken.None);

        await secEdgarClient.Received(1).GetCompanyFacts("34088");
        scopeFactory.DidNotReceive().CreateScope();
    }
}
