using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Esef;

public class EsefVisualReportTests
{
    private const string Lei = "529900S21EQ1BO4ESM68";
    private static readonly DateOnly End = new(2021, 12, 31);

    [Theory]
    [InlineData("")]
    [InlineData("﻿")]
    public void ConfirmsIdentity_ContextsNameTheIssuerAndAContextEndsOnThePeriod_IsTrue(
        string prefix
    )
    {
        var report = prefix + Report(Context(Lei, "2021-12-31") + Context(Lei, "2020-12-31"));

        EsefVisualReport.ConfirmsIdentity(report, Lei, End).Should().BeTrue();
    }

    [Fact]
    public void ConfirmsIdentity_AContextNamesAnotherIssuer_IsFalse()
    {
        var report = Report(
            Context(Lei, "2021-12-31") + Context("259400NFU8A8SBP6VC21", "2021-12-31")
        );

        EsefVisualReport.ConfirmsIdentity(report, Lei, End).Should().BeFalse();
    }

    [Fact]
    public void ConfirmsIdentity_NoContextEndsOnThePeriod_IsFalse()
    {
        EsefVisualReport
            .ConfirmsIdentity(Report(Context(Lei, "2020-12-31")), Lei, End)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void ConfirmsIdentity_BodyAnXmlReaderRefuses_IsNull()
    {
        EsefVisualReport
            .ConfirmsIdentity("<html><body><p>Unclosed</body></html>", Lei, End)
            .Should()
            .BeNull();
    }

    internal static string Report(
        string contexts,
        string prose = "Revenue grew in every market."
    ) =>
        "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\" "
        + "xmlns:xbrli=\"http://www.xbrl.org/2003/instance\"><head><title>Report</title></head><body>"
        + $"<div style=\"display:none\"><ix:header><ix:resources>{contexts}</ix:resources></ix:header></div>"
        + $"<p>{prose}</p></body></html>";

    internal static string Context(string lei, string end) =>
        "<xbrli:context id=\"c"
        + end
        + lei[..4]
        + "\"><xbrli:entity><xbrli:identifier scheme=\"http://standards.iso.org/iso/17442\">"
        + lei
        + "</xbrli:identifier></xbrli:entity><xbrli:period><xbrli:startDate>2021-01-01</xbrli:startDate>"
        + $"<xbrli:endDate>{end}</xbrli:endDate></xbrli:period></xbrli:context>";
}
