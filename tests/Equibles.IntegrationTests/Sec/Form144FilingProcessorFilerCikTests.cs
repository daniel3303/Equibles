using System.Reflection;
using System.Xml.Linq;
using Equibles.Errors.BusinessLogic;
using Equibles.InsiderTrading.BusinessLogic;
using Equibles.InsiderTrading.Data.Models;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.IntegrationTests.Sec;

/// <summary>
/// The filer CIK is what makes a Form 144 joinable to the filer's Forms 3/4/5, and therefore
/// the only way to tell whether a proposed sale was ever executed. Without it the seller is
/// free text and a name match resolves only about half of the corpus.
/// </summary>
public class Form144FilingProcessorFilerCikTests
{
    private static Form144FilingProcessor Processor() =>
        new(
            Substitute.For<IServiceScopeFactory>(),
            Substitute.For<ILogger<Form144FilingProcessor>>(),
            Substitute.For<ErrorReporter>(
                Substitute.For<IServiceScopeFactory>(),
                Substitute.For<ILogger<ErrorReporter>>()
            )
        );

    private static Form144Filing Parse(string xml)
    {
        var method = typeof(Form144FilingProcessor).GetMethod(
            "ParseFiling",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        var filing = new FilingData
        {
            AccessionNumber = "0000000000-26-000001",
            FilingDate = new DateOnly(2026, 8, 11),
            ReportDate = new DateOnly(2026, 8, 11),
            Form = "144",
        };

        return (Form144Filing)
            method.Invoke(Processor(), [XElement.Parse(xml), Guid.NewGuid(), filing]);
    }

    private static string Document(string headerData, string noticeSignature = "") =>
        "<edgarSubmission>"
        + headerData
        + "<formData>"
        + "<issuerInfo><nameOfPersonForWhoseAccountTheSecuritiesAreToBeSold>Jane Doe"
        + "</nameOfPersonForWhoseAccountTheSecuritiesAreToBeSold></issuerInfo>"
        + "<securitiesInformation><securitiesClassTitle>Common</securitiesClassTitle></securitiesInformation>"
        + noticeSignature
        + "</formData></edgarSubmission>";

    private static string Header(string cik) =>
        "<headerData><filerInfo><filer><filerCredentials><cik>"
        + cik
        + "</cik><ccc>XXXXXXXX</ccc></filerCredentials></filer></filerInfo></headerData>";

    // A notice filed through an agent still credits the natural person, not the agent. This is
    // the shape behind the great majority of the corpus.
    [Fact]
    public void ParseFiling_AgentFiledNotice_TakesThePersonsCik()
    {
        Parse(Document(Header("0001780525"))).FilerCik.Should().Be("0001780525");
    }

    // A self-filer's accession prefix happens to be their own CIK; the field is read the same way.
    [Fact]
    public void ParseFiling_SelfFiledNotice_TakesTheFilerCredentialsCik()
    {
        Parse(Document(Header("0001786391"))).FilerCik.Should().Be("0001786391");
    }

    // THE JOIN INVARIANT. InsiderOwner.OwnerCik stores the Form 4 value verbatim, and EDGAR
    // zero-pads to ten characters on both forms. Trimming the padding here, or padding it
    // differently, makes every notice-to-execution join miss silently rather than fail loudly.
    [Fact]
    public void ParseFiling_LeadingZeros_ArePreservedSoTheCikMatchesInsiderOwner()
    {
        var entity = Parse(Document(Header("0000320193")));

        entity.FilerCik.Should().Be("0000320193");
        entity.FilerCik.Should().NotBe("320193");
    }

    [Fact]
    public void ParseFiling_NoHeaderData_LeavesTheFilerCikNull()
    {
        Parse(Document(headerData: "")).FilerCik.Should().BeNull();
    }

    // Joint filings carry several <filer> elements. The first is the person the notice is filed
    // for; the parser must pick one deterministically rather than concatenating or failing.
    [Fact]
    public void ParseFiling_MultipleFilers_TakesTheFirst()
    {
        var header =
            "<headerData><filerInfo>"
            + "<filer><filerCredentials><cik>0001111111</cik></filerCredentials></filer>"
            + "<filer><filerCredentials><cik>0002222222</cik></filerCredentials></filer>"
            + "</filerInfo></headerData>";

        Parse(Document(header)).FilerCik.Should().Be("0001111111");
    }

    // A plan adoption date is the notice's own declaration that the sale is pre-arranged. It is
    // the only 10b5-1 signal available for a notice that is never executed, because there is no
    // Form 4 to borrow the flag from.
    [Fact]
    public void ParseFiling_PlanAdoptionDate_IsCaptured()
    {
        var signature =
            "<noticeSignature><noticeDate>08/11/2026</noticeDate>"
            + "<planAdoptionDates><planAdoptionDate>05/05/2026</planAdoptionDate></planAdoptionDates>"
            + "</noticeSignature>";

        Parse(Document(Header("0001780525"), signature))
            .PlanAdoptionDate.Should()
            .Be(new DateOnly(2026, 5, 5));
    }

    // Several adoption dates means an amended or layered plan; the earliest is the plan's origin.
    [Fact]
    public void ParseFiling_SeveralPlanAdoptionDates_TakesTheEarliest()
    {
        var signature =
            "<noticeSignature><planAdoptionDates>"
            + "<planAdoptionDate>09/01/2026</planAdoptionDate>"
            + "<planAdoptionDate>05/05/2026</planAdoptionDate>"
            + "</planAdoptionDates></noticeSignature>";

        Parse(Document(Header("0001780525"), signature))
            .PlanAdoptionDate.Should()
            .Be(new DateOnly(2026, 5, 5));
    }

    // Most notices declare no plan. Null must mean "none declared", never a zero date.
    [Fact]
    public void ParseFiling_NoPlanAdoptionDate_LeavesItNull()
    {
        Parse(Document(Header("0001780525"))).PlanAdoptionDate.Should().BeNull();
    }

    // The backfill parses the same two fields independently of the import path, so the two must
    // agree or history and new ingest diverge. This case feeds the ALREADY-SANITIZED payload;
    // BackfillParseIdentity_RawSgmlSubmissionAsEdgarServesIt covers what the backfill is actually
    // handed, which is not this.
    [Fact]
    public void BackfillParseIdentity_AgreesWithTheProcessor()
    {
        var signature =
            "<noticeSignature><planAdoptionDates>"
            + "<planAdoptionDate>05/05/2026</planAdoptionDate>"
            + "</planAdoptionDates></noticeSignature>";
        var xml = Document(Header("0001780525"), signature);

        var fromProcessor = Parse(xml);
        var fromBackfill = Form144FilerCikBackfillManager.ParseIdentity(xml);

        fromBackfill.FilerCik.Should().Be(fromProcessor.FilerCik);
        fromBackfill.PlanAdoptionDate.Should().Be(fromProcessor.PlanAdoptionDate);
    }

    [Fact]
    public void BackfillParseIdentity_MalformedXml_ReturnsNothingRatherThanThrowing()
    {
        var parsed = Form144FilerCikBackfillManager.ParseIdentity("<edgarSubmission");

        parsed.FilerCik.Should().BeNull();
        parsed.PlanAdoptionDate.Should().BeNull();
    }

    // The exact bytes EDGAR serves for accession 0001959173-23-000915 (trimmed), which is what
    // the backfill is handed: GetDocumentContent fetches `{accession}.txt`, the FULL submission.
    // It opens with an SGML envelope and is NOT well-formed XML, and the payload is namespaced.
    //
    // This is the regression. The backfill shipped parsing this text directly, so XDocument threw
    // on every notice, every notice looked like it "carries no filer credentials", and the lane
    // parked the corpus instead of filling it: 1,854 notices parked and 0 resolved in production
    // before it was caught. The old agreement test passed throughout because it fed the payload
    // post-sanitize, a shape the backfill never sees.
    private const string RawSgmlSubmission =
        "<SEC-DOCUMENT>0001959173-23-000915.txt : 20230508\n"
        + "<SEC-HEADER>0001959173-23-000915.hdr.sgml : 20230508\n"
        + "<ACCEPTANCE-DATETIME>20230508153241\n"
        + "ACCESSION NUMBER:\t\t0001959173-23-000915\n"
        + "CONFORMED SUBMISSION TYPE:\t144\n"
        + "</SEC-HEADER>\n"
        + "<DOCUMENT>\n<TYPE>144\n<SEQUENCE>1\n<FILENAME>primary_doc.xml\n<TEXT>\n"
        + "<XML>\n"
        + "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
        + "<edgarSubmission xmlns=\"http://www.sec.gov/edgar/ownership\" "
        + "xmlns:com=\"http://www.sec.gov/edgar/common\">"
        + "<headerData><filerInfo><filer><filerCredentials>"
        + "<cik>0001914614</cik><ccc>XXXXXXXX</ccc>"
        + "</filerCredentials></filer></filerInfo></headerData>"
        + "<formData><noticeSignature><planAdoptionDates>"
        + "<planAdoptionDate>11/28/2022</planAdoptionDate>"
        + "</planAdoptionDates></noticeSignature></formData>"
        + "</edgarSubmission>\n"
        + "</XML>\n</TEXT>\n</DOCUMENT>\n</SEC-DOCUMENT>\n";

    [Fact]
    public void BackfillParseIdentity_RawSgmlSubmissionAsEdgarServesIt_ReadsFilerCikAndPlanDate()
    {
        var parsed = Form144FilerCikBackfillManager.ParseIdentity(RawSgmlSubmission);

        // Zero-padded verbatim, so it joins InsiderOwner.OwnerCik without transformation.
        parsed.FilerCik.Should().Be("0001914614");
        parsed.PlanAdoptionDate.Should().Be(new DateOnly(2022, 11, 28));
    }

    // A document EDGAR serves that genuinely carries no credentials must still park, so the fix
    // above cannot be mistaken for "never park anything".
    [Fact]
    public void BackfillParseIdentity_SgmlSubmissionWithoutFilerCredentials_ReturnsNothing()
    {
        var withoutCredentials =
            "<SEC-DOCUMENT>x.txt : 20230508\n<SEC-HEADER>x</SEC-HEADER>\n<XML>\n"
            + "<edgarSubmission xmlns=\"http://www.sec.gov/edgar/ownership\">"
            + "<headerData><filerInfo><filer /></filerInfo></headerData>"
            + "</edgarSubmission>\n</XML>\n</SEC-DOCUMENT>\n";

        Form144FilerCikBackfillManager.ParseIdentity(withoutCredentials).FilerCik.Should().BeNull();
    }
}
