using Equibles.Sec.Data.Models;
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
        var result = FormAdvSnapshotCatalog.Latest(html, FormAdvRegistration.Registered);

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
        var result = FormAdvSnapshotCatalog.Latest(html, FormAdvRegistration.Registered);

        result.ReportDate.Should().Be(new DateOnly(2026, 8, 1));
        result.Url.Should().Be("https://www.sec.gov/files/ia08032026_1.zip");
    }

    [Fact]
    public void Latest_OfficialCatalogue_UsesPublishedExemptUrlAndMonth()
    {
        var html = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Sec", "form-adv-catalog.html")
        );
        var result = FormAdvSnapshotCatalog.Latest(html, FormAdvRegistration.ExemptReporting);

        result.ReportDate.Should().Be(new DateOnly(2026, 9, 1));
        result
            .Url.Should()
            .Be(
                "https://www.sec.gov/files/investment/data/other/information-about-registered-investment-advisers-exempt-reporting-advisers/ia09012026-exempt.zip"
            );
    }

    [Fact]
    public void Latest_NoDataNoticeMonth_FallsBackToThePreviousRoster()
    {
        // SEC published October and November 2025 as "no data" PDF notices instead of rosters.
        var html = """
            <a href="/files/ia-no-data-110125.pdf">Registered Investment Advisers, November 2025</a>
            <a href="/files/ia-no-data-100125.pdf">Registered Investment Advisers, October 2025</a>
            <a href="/files/ia090225.zip">Registered Investment Advisers, September 2025</a>
            """;

        var result = FormAdvSnapshotCatalog.Latest(html, FormAdvRegistration.Registered);

        result.ReportDate.Should().Be(new DateOnly(2025, 9, 1));
        result.Url.Should().Be("https://www.sec.gov/files/ia090225.zip");
    }

    [Fact]
    public void Latest_OnlyNoDataNotices_Refuses()
    {
        var html =
            "<a href=\"/files/ia-no-data-110125-exempt.pdf\">Exempt Investment Advisers, November 2025</a>";

        var act = () => FormAdvSnapshotCatalog.Latest(html, FormAdvRegistration.ExemptReporting);

        act.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("https://untrusted.test/files/data.zip")]
    [InlineData("http://www.sec.gov/files/data.zip")]
    [InlineData("https://www.sec.gov:8443/files/data.zip")]
    [InlineData("https://user@www.sec.gov/files/data.zip")]
    [InlineData("/Archives/data.zip")]
    [InlineData("/files/latest.csv")]
    public void Latest_UnsafeOrUnsupportedNewestLink_RefusesInsteadOfFallingBack(string href)
    {
        var html =
            $"<a href=\"{href}\">Registered Investment Advisers, September 2026</a>"
            + "<a href=\"/files/older.zip\">Registered Investment Advisers, August 2026</a>";

        var act = () => FormAdvSnapshotCatalog.Latest(html, FormAdvRegistration.Registered);

        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Latest_BareWorkbookRoster_IsUsed()
    {
        // SEC published rosters from mid 2023 to late 2025 as a bare workbook, not a ZIP.
        var html =
            "<a href=\"/files/ia090225.xlsx\">Registered Investment Advisers, September 2025</a>";

        var result = FormAdvSnapshotCatalog.Latest(html, FormAdvRegistration.Registered);

        result.Url.Should().Be("https://www.sec.gov/files/ia090225.xlsx");
    }

    [Fact]
    public void All_ListsEveryRosterSinceTheStartOldestFirst_SkippingNoDataNotices()
    {
        var html = """
            <a href="/files/ia120125.zip">Registered Investment Advisers, December 2025</a>
            <a href="/files/ia-no-data-110125.pdf">Registered Investment Advisers, November 2025</a>
            <a href="/files/ia090225.xlsx">Registered Investment Advisers, September 2025</a>
            <a href="/files/ia120219.zip">Registered Investment Advisers, December 2019</a>
            <a href="/files/ia110119.zip">Registered Investment Advisers, November 2019</a>
            <a href="/files/ia020124-exempt.xlsx">Exempt Investment Advisers, February 2024</a>
            """;

        var registered = FormAdvSnapshotCatalog.All(
            html,
            FormAdvRegistration.Registered,
            new DateOnly(2019, 12, 1)
        );

        registered
            .Select(roster => (roster.ReportDate, roster.Url))
            .Should()
            .Equal(
                (new DateOnly(2019, 12, 1), "https://www.sec.gov/files/ia120219.zip"),
                (new DateOnly(2025, 9, 1), "https://www.sec.gov/files/ia090225.xlsx"),
                (new DateOnly(2025, 12, 1), "https://www.sec.gov/files/ia120125.zip")
            );
        FormAdvSnapshotCatalog
            .All(html, FormAdvRegistration.ExemptReporting, new DateOnly(2019, 12, 1))
            .Should()
            .ContainSingle()
            .Which.ReportDate.Should()
            .Be(new DateOnly(2024, 2, 1));
    }

    [Fact]
    public void All_AmbiguousMonth_IsPassedOverLikeAMonthWithoutData()
    {
        var html =
            "<a href='/files/a.zip'>Registered Investment Advisers, January 2020</a>"
            + "<a href='/files/b.zip'>Registered Investment Advisers, January 2020</a>"
            + "<a href='/files/c.zip'>Registered Investment Advisers, February 2020</a>";

        var rosters = FormAdvSnapshotCatalog.All(
            html,
            FormAdvRegistration.Registered,
            new DateOnly(2019, 12, 1)
        );

        rosters.Should().ContainSingle().Which.Url.Should().Be("https://www.sec.gov/files/c.zip");
    }

    [Fact]
    public void All_LinkOutsideSecFiles_Refuses()
    {
        var html =
            "<a href='/files/a.zip'>Registered Investment Advisers, January 2020</a>"
            + "<a href='https://untrusted.test/files/b.zip'>Registered Investment Advisers, February 2020</a>";

        var act = () =>
            FormAdvSnapshotCatalog.All(
                html,
                FormAdvRegistration.Registered,
                new DateOnly(2019, 12, 1)
            );

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
        var act = () => FormAdvSnapshotCatalog.Latest(html, FormAdvRegistration.Registered);

        act.Should().Throw<InvalidDataException>();
    }
}
