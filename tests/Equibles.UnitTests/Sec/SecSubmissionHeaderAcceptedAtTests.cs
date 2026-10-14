using Equibles.Sec.BusinessLogic;

namespace Equibles.UnitTests.Sec;

// EDGAR stamps ACCEPTANCE-DATETIME in Eastern wall-clock time; the stored value is UTC, so both
// daylight and standard offsets must convert, and anything outside the SEC-HEADER must not count.
public class SecSubmissionHeaderAcceptedAtTests
{
    private static string Submission(string headerStamp, string body = "<html>filing</html>") =>
        "<SEC-DOCUMENT>0001140361-26-039158.txt : 20261008\n"
        + "<SEC-HEADER>0001140361-26-039158.hdr.sgml : 20261008\n"
        + (headerStamp == null ? "" : $"<ACCEPTANCE-DATETIME>{headerStamp}\n")
        + "ACCESSION NUMBER:\t\t0001140361-26-039158\n"
        + "</SEC-HEADER>\n"
        + $"<DOCUMENT>\n<TYPE>4\n<TEXT>\n{body}\n</TEXT>\n</DOCUMENT>\n"
        + "</SEC-DOCUMENT>";

    // The real header of 0001140361-26-039158, accepted 18:30:32 EDT.
    [Fact]
    public void AcceptedAt_DaylightTimeStamp_ConvertsToUtc()
    {
        SecSubmissionHeader
            .AcceptedAt(Submission("20261008183032"))
            .Should()
            .Be(new DateTime(2026, 10, 8, 22, 30, 32, DateTimeKind.Utc));
    }

    [Fact]
    public void AcceptedAt_StandardTimeStamp_ConvertsToUtc()
    {
        var acceptedAt = SecSubmissionHeader.AcceptedAt(Submission("20260129163015"));

        acceptedAt.Should().Be(new DateTime(2026, 1, 29, 21, 30, 15, DateTimeKind.Utc));
        acceptedAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void AcceptedAt_TagQuotedOnlyInADocumentBody_IsNull()
    {
        var submission = Submission(null, body: "<ACCEPTANCE-DATETIME>20261008183032");

        SecSubmissionHeader.AcceptedAt(submission).Should().BeNull();
    }

    [Theory]
    [InlineData("2026100818")]
    [InlineData("20261308183032")]
    [InlineData("20260308023000")] // 02:30 on the spring-forward night never happened in New York.
    public void AcceptedAt_InvalidStamp_IsNull(string stamp)
    {
        SecSubmissionHeader.AcceptedAt(Submission(stamp)).Should().BeNull();
    }

    [Fact]
    public void AcceptedAt_NoHeader_IsNull()
    {
        SecSubmissionHeader.AcceptedAt("<html>not a submission</html>").Should().BeNull();
        SecSubmissionHeader.AcceptedAt(null).Should().BeNull();
    }
}
