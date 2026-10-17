using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// SEC's N-CEN data set catalogue: every quarter's archive, oldest first, with the file name that
/// tells a re-posted quarter apart, and refusals for an ambiguous quarter or a foreign link.
/// </summary>
public class NCenDataSetCatalogTests
{
    [Fact]
    public void Parse_ListsEveryQuarterOldestFirst_WithItsFileName()
    {
        const string html = """
            <a href="/files/dera/data/form-n-cen-data-sets/2026q3_ncen.zip" download>2026 Q3 NCEN</a>
            <a href="/files/dera/data/form-n-cen-data-sets/2025q2_ncen_0.zip" download>2025 Q2 NCEN</a>
            <a href="/files/dera/data/form-n-cen-data-sets/2018q3_ncen.zip" download>2018 Q3 NCEN</a>
            <a href="/files/other.zip">Other</a>
            """;

        var dataSets = NCenDataSetCatalog.Parse(html);

        dataSets.Select(d => (d.Year, d.Quarter)).Should().Equal((2018, 3), (2025, 2), (2026, 3));
        var reposted = dataSets.Single(d => d.Year == 2025);
        reposted.FileName.Should().Be("2025q2_ncen_0.zip");
        reposted
            .Url.Should()
            .Be("https://www.sec.gov/files/dera/data/form-n-cen-data-sets/2025q2_ncen_0.zip");
    }

    [Fact]
    public void Parse_TwoArchivesForOneQuarter_IsRefused()
    {
        const string html = """
            <a href="/files/dera/data/form-n-cen-data-sets/2025q2_ncen.zip">2025 Q2</a>
            <a href="/files/dera/data/form-n-cen-data-sets/2025q2_ncen_0.zip">2025 Q2</a>
            """;

        var act = () => NCenDataSetCatalog.Parse(html);

        act.Should().Throw<InvalidDataException>().WithMessage("*2025 Q2*");
    }

    [Fact]
    public void Parse_TheSameArchiveListedTwice_IsOneQuarter()
    {
        const string html = """
            <a href="/files/dera/data/form-n-cen-data-sets/2026q3_ncen.zip">2026 Q3</a>
            <a href="https://www.sec.gov/files/dera/data/form-n-cen-data-sets/2026q3_ncen.zip">2026 Q3</a>
            """;

        NCenDataSetCatalog.Parse(html).Should().ContainSingle();
    }

    [Fact]
    public void Parse_ArchiveOnAnotherHost_IsRefused()
    {
        const string html = """
            <a href="https://example.com/files/dera/data/form-n-cen-data-sets/2026q3_ncen.zip">2026 Q3</a>
            """;

        var act = () => NCenDataSetCatalog.Parse(html);

        act.Should().Throw<InvalidDataException>().WithMessage("*official file directory*");
    }

    [Fact]
    public void Parse_PageWithoutArchives_IsRefused()
    {
        var act = () => NCenDataSetCatalog.Parse("<html><a href='/files/x.zip'>x</a></html>");

        act.Should().Throw<InvalidDataException>();
    }
}
