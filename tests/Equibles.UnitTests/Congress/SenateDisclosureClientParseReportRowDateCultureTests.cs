using System.Globalization;
using System.Reflection;
using Equibles.Congress.HostedService.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Equibles.UnitTests.Congress;

public class SenateDisclosureClientParseReportRowDateCultureTests
{
    private static readonly MethodInfo ParseReportRowMethod =
        typeof(SenateDisclosureClient).GetMethod(
            "ParseReportRow",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

    // eFD dates are US MM/dd/yyyy. Parsing the row with the host culture drops
    // every day-ambiguous report on a dd/MM host (08/20/2025 has no month 20),
    // silently skipping the filing — the row must parse identically everywhere.
    [Fact]
    public void ParseReportRow_UsDateOnNonUsHostCulture_StillParsesTheReport()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-PT");
        try
        {
            var sut = new SenateDisclosureClient(
                Substitute.For<ISenateBrowserSession>(),
                Substitute.For<ILogger<SenateDisclosureClient>>()
            );
            var row = new List<string>
            {
                "Jane",
                "Doe",
                "Doe, Jane (Senator)",
                "<a href=\"/search/view/ptr/3a3528d3-9133-4e54-9af6-31effe2a69e7/\">Periodic Transaction Report</a>",
                "08/20/2025",
            };

            var report = ParseReportRowMethod.Invoke(sut, [row]);

            report.Should().NotBeNull("a US-format date must parse regardless of host culture");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }
}
