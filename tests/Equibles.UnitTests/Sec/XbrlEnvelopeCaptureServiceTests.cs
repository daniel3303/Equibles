using System.Text;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class XbrlEnvelopeCaptureServiceTests
{
    private const string InlineBody =
        "<html xmlns:ix=\"http://www.xbrl.org/2013/inlineXBRL\"><body><ix:nonFraction>1</ix:nonFraction></body></html>";

    private static string InlineSubmission(string primaryFileName) =>
        $"""
            <DOCUMENT>
            <TYPE>10-K
            <FILENAME>{primaryFileName}
            <TEXT>
            {InlineBody}
            </TEXT>
            </DOCUMENT>
            """;

    private static FilingData Filing() =>
        new()
        {
            Cik = "0000320193",
            AccessionNumber = "0000320193-18-000145",
            Form = "10-K",
            PrimaryDocument = "aapl-10k.htm",
        };

    private static XbrlEnvelopeCaptureService Build(bool enabled) =>
        new(
            Options.Create(new XbrlCaptureOptions { Enabled = enabled }),
            Substitute.For<ILogger<XbrlEnvelopeCaptureService>>()
        );

    [Fact]
    public void Capture_Disabled_ReturnsNotChecked()
    {
        var result = Build(enabled: false).Capture(InlineSubmission("aapl-10k.htm"), Filing());

        result.Status.Should().Be(XbrlCaptureStatus.NotChecked);
        result.RawBytes.Should().BeNull();
    }

    [Fact]
    public void Capture_EnabledWithInlineXbrl_ReturnsCapturedWithGzippableBytes()
    {
        var result = Build(enabled: true).Capture(InlineSubmission("aapl-10k.htm"), Filing());

        result.Status.Should().Be(XbrlCaptureStatus.Captured);
        result.Type.Should().Be(XbrlType.InlineIxbrl);
        result.SourceFileName.Should().Be("aapl-10k.htm");
        result.RawBytes.Should().Equal(Encoding.UTF8.GetBytes(InlineBody));
    }

    [Fact]
    public void Capture_EnabledButNoXbrl_ReturnsNotPresent()
    {
        var submission =
            "<DOCUMENT>\n<TYPE>10-K\n<FILENAME>aapl-10k.htm\n<TEXT>\n<html><body>plain</body></html>\n</TEXT>\n</DOCUMENT>";

        var result = Build(enabled: true).Capture(submission, Filing());

        result.Status.Should().Be(XbrlCaptureStatus.NotPresent);
        result.RawBytes.Should().BeNull();
    }
}
