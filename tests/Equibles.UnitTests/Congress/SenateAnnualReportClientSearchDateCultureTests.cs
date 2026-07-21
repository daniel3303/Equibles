using System.Globalization;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

public class SenateAnnualReportClientSearchDateCultureTests
{
    [Fact]
    public async Task GetAnnualReports_NonUsHostCulture_SendsUsFormattedSubmittedDates()
    {
        // eFD only understands US MM/dd/yyyy dates. The "MM/dd/yyyy" custom
        // format substitutes the host culture's date separator, so a de-DE
        // host would post "01.01.2025" and the search silently returns
        // nothing — the request must be culture-pinned.
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            Dictionary<string, string> captured = null;
            var session = Substitute.For<ISenateBrowserSession>();
            session
                .Fetch(
                    Arg.Any<string>(),
                    Arg.Do<Dictionary<string, string>>(f => captured = f),
                    Arg.Any<CancellationToken>()
                )
                .Returns(
                    new SenateFetchResult
                    {
                        Status = 200,
                        Body = """{"recordsTotal":0,"data":[]}""",
                    }
                );
            var sut = new SenateAnnualReportClient(
                session,
                Substitute.For<ILogger<SenateAnnualReportClient>>()
            );

            await sut.GetAnnualReports(
                new DateOnly(2025, 1, 1),
                new DateOnly(2025, 12, 31),
                new HashSet<string>(),
                CancellationToken.None
            );

            captured.Should().NotBeNull();
            captured["submitted_start_date"].Should().Be("01/01/2025 00:00:00");
            captured["submitted_end_date"].Should().Be("12/31/2025 23:59:59");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
