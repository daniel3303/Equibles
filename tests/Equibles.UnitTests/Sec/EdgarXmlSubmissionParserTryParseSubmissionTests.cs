using System.Xml.Linq;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.HostedService.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class EdgarXmlSubmissionParserTryParseSubmissionTests
{
    [Fact]
    public async Task TryParseSubmission_MalformedXmlWithEdgarRoot_ReturnsNullWithoutThrowing()
    {
        // Contract (from the XML-doc, not the body): the method returns null when the filing
        // "is malformed (logging and reporting the case)". A submission that contains the
        // <edgarSubmission> root passes the non-XML skip guard and reaches XDocument.Parse, so
        // the only way to honor the contract is to catch the parse failure. The mismatched
        // </edgarSubmission> closing an open <reportingOwner> makes XDocument.Parse throw — that
        // exception must be caught and turned into null, never allowed to escape to the caller.
        var malformed = "<XML><edgarSubmission><reportingOwner></edgarSubmission></XML>";
        var filing = new FilingData
        {
            Cik = "0000320193",
            AccessionNumber = "0000320193-24-000001",
        };
        var errorReporter = new ErrorReporter(
            Substitute.For<IServiceScopeFactory>(),
            NullLogger<ErrorReporter>.Instance
        );

        XElement result = null;
        var act = async () =>
            result = await EdgarXmlSubmissionParser.TryParseSubmission(
                malformed,
                filing,
                "AAPL",
                "Form 144",
                "Form144.ParseXml",
                NullLogger.Instance,
                errorReporter
            );

        await act.Should()
            .NotThrowAsync("malformed XML must be caught and reported, not propagated");
        result.Should().BeNull();
    }
}
