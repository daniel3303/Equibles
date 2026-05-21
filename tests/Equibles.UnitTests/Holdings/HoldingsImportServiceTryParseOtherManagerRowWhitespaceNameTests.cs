using System.Reflection;
using Equibles.Core.Configuration;
using Equibles.Core.Contracts;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Holdings;

public class HoldingsImportServiceTryParseOtherManagerRowWhitespaceNameTests
{
    // TryParseOtherManagerRow (extracted in #1539) gates the NAME field with
    // `string.IsNullOrEmpty(name)` — catching null and "" but letting
    // whitespace-only "   " through. Per the strict-null-on-malformed
    // precedent (GH-1350 IsRecentFtdFile, GH-1438 FtdImportService.ParseLine,
    // GH-1514 TryBuildParsedFact), a SEC OTHERMANAGER2.tsv row carrying a
    // blanked NAME is malformed and should be rejected — not surfaced
    // downstream where it lands in the (Accession → SequenceNumber → Name)
    // map and gets attached to filings as a "valid manager named '   '".
    [Fact(
        Skip = "GH-1544 — TryParseOtherManagerRow accepts whitespace-only NAME instead of rejecting"
    )]
    public void TryParseOtherManagerRow_WhitespaceOnlyName_ReturnsFalse()
    {
        var sut = new HoldingsImportService(
            Substitute.For<IServiceScopeFactory>(),
            NullLogger<HoldingsImportService>.Instance,
            Options.Create(new WorkerOptions()),
            Substitute.For<IStockPriceProvider>()
        );
        var accession = "0000950123-24-006477";
        var submissions = new Dictionary<string, SubmissionRow>
        {
            [accession] = new() { AccessionNumber = accession },
        };
        var row = new Dictionary<string, string>
        {
            ["ACCESSION_NUMBER"] = accession,
            ["SEQUENCENUMBER"] = "1",
            ["NAME"] = "   ",
        };

        var method = typeof(HoldingsImportService).GetMethod(
            "TryParseOtherManagerRow",
            BindingFlags.NonPublic | BindingFlags.Instance
        );
        var args = new object[] { row, submissions, null, 0, null };

        var resolved = (bool)method.Invoke(sut, args);

        resolved.Should().BeFalse();
    }
}
