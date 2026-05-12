using Equibles.Sec.BusinessLogic;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Sec;

public class PdfTextExtractorTests {
    [Fact]
    public void Extract_MalformedBytes_ReturnsEmptyAndLogsWarning() {
        var logger = Substitute.For<ILogger<PdfTextExtractor>>();
        var sut = new PdfTextExtractor(logger);
        var notAPdf = new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }; // "GIF89a" magic

        var result = sut.Extract(notAPdf);

        result.Should().BeEmpty();
        logger.ReceivedCalls()
            .Any(c => c.GetMethodInfo().Name == "Log"
                && c.GetArguments().OfType<LogLevel>().FirstOrDefault() == LogLevel.Warning)
            .Should().BeTrue();
    }
}
