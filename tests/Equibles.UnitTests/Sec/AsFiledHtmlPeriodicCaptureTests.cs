using System.Text;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Configuration;
using Equibles.Sec.HostedService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class AsFiledHtmlPeriodicCaptureTests
{
    [Theory]
    [InlineData("Ebs2025Annual.html.gz", "10-K", "ebs-20251231.htm", "0001367644-26-000015", 16)]
    [InlineData("Ebs2026Quarterly.html.gz", "10-Q", "ebs-20260630.htm", "0001367644-26-000082", 1)]
    public async Task Capture_PeriodicPrimaryWithoutExhibits_RetainsItsHtmlAndEveryImage(
        string fixture,
        string form,
        string fileName,
        string accession,
        int expectedImages
    )
    {
        var compressed = File.ReadAllBytes(
            Path.Combine(
                AppContext.BaseDirectory,
                "TestAssets",
                "Sec",
                "PeriodicOriginals",
                fixture
            )
        );
        var original = Encoding.UTF8.GetString(GzipCompressor.Decompress(compressed));
        var envelope = Submission(form, fileName, original);
        var client = Substitute.For<ISecEdgarClient>();
        byte[] image = [1, 2, 3];
        client
            .GetDocumentFileBytes(
                "1367644",
                accession,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(image);
        var service = new AsFiledHtmlCaptureService(
            Options.Create(new AsFiledHtmlCaptureOptions()),
            client,
            NullLogger<AsFiledHtmlCaptureService>.Instance
        );

        AsFiledHtmlCaptureService.AppliesTo(DocumentType.FromDisplayName(form)).Should().BeTrue();
        var captured = await service.Capture(
            envelope,
            new FilingData
            {
                Cik = "1367644",
                AccessionNumber = accession,
                PrimaryDocument = fileName,
            }
        );

        captured.Html.Should().NotBeNullOrEmpty();
        var capturedHtml = Encoding.UTF8.GetString(captured.Html);
        capturedHtml.Should().Contain($"data-asfiled-file=\"{fileName}\"");
        captured.Images.Should().HaveCount(expectedImages);
        captured.Images.Should().OnlyContain(value => value.Bytes.SequenceEqual(image));
        var prefix = fileName[..^4];
        captured
            .Images.Select(value => value.FileName)
            .Should()
            .BeEquivalentTo(
                Enumerable.Range(1, expectedImages).Select(number => $"{prefix}_g{number}.jpg")
            );
        await client
            .Received(expectedImages)
            .GetDocumentFileBytes(
                "1367644",
                accession,
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("10-K/A")]
    [InlineData("10-Q/A")]
    [InlineData("20-F")]
    [InlineData("20-F/A")]
    [InlineData("40-F")]
    [InlineData("40-F/A")]
    [InlineData("6-K")]
    [InlineData("6-K/A")]
    public void TryBuildAsFiledHtml_PeriodicPrimary_RetainsImages(string form)
    {
        var envelope = Submission(
            form,
            "report.htm",
            "<html><body><img src=\"chart.png\"></body></html>"
        );

        var built = SecDocumentEnvelopeParser.TryBuildAsFiledHtml(
            envelope,
            "report.htm",
            out var html,
            out var images
        );

        built.Should().BeTrue();
        html.Should().Contain("data-asfiled-file=\"report.htm\"");
        images.Should().Equal("chart.png");
    }

    [Theory]
    [InlineData("8-K", "report.htm")]
    [InlineData("4", "report.htm")]
    [InlineData("10-K", "report.xml")]
    public void TryBuildAsFiledHtml_UnsupportedPrimaryOnlyShape_RemainsUnavailable(
        string form,
        string fileName
    )
    {
        SecDocumentEnvelopeParser
            .TryBuildAsFiledHtml(
                Submission(form, fileName, "<html><body>Report</body></html>"),
                fileName,
                out _
            )
            .Should()
            .BeFalse();
    }

    [Fact]
    public void TryBuildAsFiledHtml_OnlyExhibits_DoesNotInventAPrimaryReport()
    {
        var envelope = Submission("EX-99.1", "first.htm", "<html><body>Exhibit</body></html>")
            .Replace(
                "</SEC-DOCUMENT>",
                "<DOCUMENT>\n<TYPE>EX-99.2\n<FILENAME>second.htm\n<TEXT><html><body>Other exhibit</body></html></TEXT>\n</DOCUMENT>\n</SEC-DOCUMENT>"
            );

        SecDocumentEnvelopeParser.TryBuildAsFiledHtml(envelope, null, out _).Should().BeFalse();
    }

    private static string Submission(string form, string fileName, string html) => $"""
        <SEC-DOCUMENT>
        <DOCUMENT>
        <TYPE>{form}
        <SEQUENCE>1
        <FILENAME>{fileName}
        <TEXT>
        {html}
        </TEXT>
        </DOCUMENT>
        </SEC-DOCUMENT>
        """;
}
