using Equibles.CommonStocks.Data.Models;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.HostedService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class ReportedStatementsCaptureServiceEarlierCikTests
{
    // The issuer took a new CIK after this filing. EDGAR answers nothing for its summary under
    // the new one, which reads as a filing without statements and settles it for good.
    [Fact]
    public async Task Capture_FilingStoredUnderAnEarlierCik_AsksForItsSummaryUnderTheCikOfItsAddress()
    {
        var edgar = Substitute.For<ISecEdgarClient>();
        var service = new ReportedStatementsCaptureService(
            edgar,
            Substitute.For<IFileManager>(),
            NullLogger<ReportedStatementsCaptureService>.Instance
        );
        var document = new Document
        {
            AccessionNumber = "0001654954-23-004613",
            SourceUrl =
                "https://www.sec.gov/Archives/edgar/data/0001593538/0001654954-23-004613.txt",
            Issuer = new EquityIssuer { Cik = "2152623" },
        };

        await service.Capture(document, maxParallelFetches: 1, CancellationToken.None);

        await edgar
            .Received(1)
            .GetDocumentFileBytes(
                "1593538",
                "0001654954-23-004613",
                "FilingSummary.xml",
                Arg.Any<CancellationToken>()
            );
        await edgar
            .DidNotReceive()
            .GetDocumentFileBytes(
                "2152623",
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }
}
