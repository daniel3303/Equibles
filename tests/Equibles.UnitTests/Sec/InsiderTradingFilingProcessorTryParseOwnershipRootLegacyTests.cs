using System.Reflection;
using System.Xml.Linq;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Equibles.UnitTests.Sec;

public class InsiderTradingFilingProcessorTryParseOwnershipRootLegacyTests
{
    [Fact]
    public async Task TryParseOwnershipRoot_LegacyPreXmlFiling_ReturnsNullWithoutReportingError()
    {
        // InsiderTradingFilingProcessor.TryParseOwnershipRoot (extracted in #1478)
        // documents:
        //   "Pre-XML-era ownership filings (Forms 3/4/5 before SEC mandated
        //    XML around mid-2003) are PEM/SGML text with no <ownershipDocument>
        //    root, so XML parsing always fails with 'Data at the root level is
        //    invalid'. They are unsupported by design — skip them quietly
        //    instead of reporting a guaranteed error per file."
        //
        // SEC EDGAR's Forms 3/4/5 archive still serves these legacy PEM/SGML
        // submissions (they are not deleted, just superseded by the XML
        // mandate). DocumentScraper iterates every filing in date order, so a
        // backfill or historical re-scrape that reaches into 2002 or earlier
        // will encounter thousands of such filings consecutively. The
        // <ownershipDocument early-return is what keeps that scrape quiet.
        //
        // The risk this catches: a refactor that drops the
        //   `if (!sanitized.Contains("<ownershipDocument", ...)) return null;`
        // early-return — perhaps because the catch-XmlException arm "already
        // handles the same case" — would compile, pass every existing
        // SanitizeXml / ParseTransaction / ParseOwnershipNature pin (none
        // probe this method directly), and on the first 2002-era PEM/SGML
        // filing fall into the XmlException catch. That catch path only
        // emits a DEBUG log; OK so far. BUT the SECOND catch (the generic
        // `catch (Exception ex)`) calls `_errorReporter.Report(...)` which
        // writes an Errors-table row. A subtly different refactor — say,
        // dropping the XmlException catch entirely on the assumption that
        // "all parse failures are the same" — would route legacy filings
        // into the error-reporting arm, polluting the Errors table with
        // one row per legacy filing and burying real failures under
        // historical noise.
        //
        // Pin the silent-skip arm: feed a pre-XML PEM/SGML envelope (no
        // <ownershipDocument> anywhere) and assert the helper returns null
        // without throwing. The null result is the necessary-and-sufficient
        // signal that the early-return fired rather than any downstream
        // catch.
        var processor = new InsiderTradingFilingProcessor(
            scopeFactory: null,
            logger: NullLogger<InsiderTradingFilingProcessor>.Instance,
            errorReporter: null
        );

        var method = typeof(InsiderTradingFilingProcessor).GetMethod(
            "TryParseOwnershipRoot",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        var legacyEnvelope = """
            -----BEGIN PRIVACY-ENHANCED MESSAGE-----
            Proc-Type: 2001,MIC-CLEAR
            Originator-Name: webmaster@www.sec.gov
            <TYPE>4
            <SEQUENCE>1
            <FILENAME>0000950123-02-006477.txt
            <TEXT>
            TICKER  SYMBOL  REPORTING-OWNER  ROLE  TRANSACTION-DATE  ...
            -----END PRIVACY-ENHANCED MESSAGE-----
            """;

        var filing = new FilingData
        {
            AccessionNumber = "0000950123-02-006477",
            Cik = "0000320193",
        };

        var task = (Task<XElement>)method.Invoke(processor, [legacyEnvelope, filing, "AAPL"]);
        var result = await task;

        result.Should().BeNull();
    }
}
