using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FormAdvSnapshotCatalogTests
{
    [Fact]
    public void Latest_OfficialCatalogue_UsesPublishedRegisteredUrlAndMonth()
    {
        var html = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Sec", "form-adv-catalog.html")
        );
        var result = FormAdvSnapshotCatalog.Latest(html);

        result.ReportDate.Should().Be(new DateOnly(2026, 9, 1));
        result
            .Url.Should()
            .Be(
                "https://www.sec.gov/files/investment/data/other/information-about-registered-investment-advisers-exempt-reporting-advisers/ia09012026-registered.zip"
            );
    }

    [Fact]
    public void Latest_UnorderedCatalogueWithNewerExemptFile_UsesRegisteredMonthAndExactRevisionUrl()
    {
        var html = """
            <a href="/files/ia07012026.zip">Registered Investment Advisers, July 2026</a>
            <a href="/files/ia09012026-exempt.zip">Exempt Investment Advisers, September 2026</a>
            <a href="/files/ia08032026_1.zip">Registered Investment Advisers, August 2026</a>
            <a href="/foia/docs/older.xls">Registered Investment Advisers, July 2006</a>
            """;
        var result = FormAdvSnapshotCatalog.Latest(html);

        result.ReportDate.Should().Be(new DateOnly(2026, 8, 1));
        result.Url.Should().Be("https://www.sec.gov/files/ia08032026_1.zip");
    }

    [Theory]
    [InlineData("https://untrusted.test/files/data.zip")]
    [InlineData("http://www.sec.gov/files/data.zip")]
    [InlineData("https://www.sec.gov:8443/files/data.zip")]
    [InlineData("https://user@www.sec.gov/files/data.zip")]
    [InlineData("/Archives/data.zip")]
    [InlineData("/files/latest.xlsx")]
    public void Latest_UnsafeOrUnsupportedNewestLink_RefusesInsteadOfFallingBack(string href)
    {
        var html =
            $"<a href=\"{href}\">Registered Investment Advisers, September 2026</a>"
            + "<a href=\"/files/older.zip\">Registered Investment Advisers, August 2026</a>";

        var act = () => FormAdvSnapshotCatalog.Latest(html);

        act.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("<html>Access denied</html>")]
    [InlineData("<a href='/files/new.zip'>Registered Investment Advisers, Not a month</a>")]
    [InlineData(
        "<a href='/files/a.zip'>Registered Investment Advisers, September 2026</a><a href='/files/b.zip'>Registered Investment Advisers, September 2026</a>"
    )]
    public void Latest_MissingOrAmbiguousCatalogue_Refuses(string html)
    {
        var act = () => FormAdvSnapshotCatalog.Latest(html);

        act.Should().Throw<InvalidDataException>();
    }
}
