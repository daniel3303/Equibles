using System.IO.Compression;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

namespace Equibles.UnitTests.Sec;

public class FiscalCalendarCapturedEnvelopeTests
{
    [Fact]
    public void AiotInlineFiling_RecoversDeclaredMarchYearEnd()
    {
        var evidence = new InlineXbrlParser()
            .ParseEnvelope(Read("aiot-0001628280-26-055222.gz"))
            .FiscalYearEnds;
        evidence
            .Should()
            .Contain(e =>
                e.Cik == "0001774170"
                && e.PeriodEnd == new DateOnly(2026, 6, 30)
                && e.Month == 3
                && e.Day == 31
            );
    }

    [Fact]
    public void ChptStandaloneFiling_RecoversPreTransitionYearEndFromSgmlWrappedInstance()
    {
        var evidence = new StandaloneXbrlParser().ParseFiscalYearEnds(
            Read("chpt-0001213900-21-008053.gz")
        );
        evidence.Should().ContainSingle();
        evidence[0].PeriodEnd.Should().Be(new DateOnly(2020, 12, 31));
        evidence[0].Month.Should().Be(12);
        evidence[0].Day.Should().Be(31);
    }

    private static string Read(string file)
    {
        using var source = File.OpenRead(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Sec", "FiscalCalendars", file)
        );
        using var gzip = new GZipStream(source, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }
}
