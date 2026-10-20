using System.IO.Compression;
using System.Text;
using Equibles.Integrations.Sec.FormAdv;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

/// <summary>
/// Record-replay pins for the older roster shapes the history walk reads, against real rows cut
/// from SEC's December 2019 registered roster (an .xlsx inside a ZIP, the 262-column layout
/// without CIK or private-fund counts) and June 2023 roster (a bare .xlsx, the 448-column
/// layout): both parse to the same typed fields as the CSV rosters.
/// </summary>
public class FormAdvRosterFileTests
{
    private static string Asset(string file) =>
        Path.Combine(AppContext.BaseDirectory, "TestAssets", "FormAdv", file);

    private static Dictionary<int, FormAdvAdviserData> Parse(Stream download, string url) =>
        FormAdvCsvParser
            .Parse(FormAdvRosterFile.Open(download, url))
            .ToDictionary(adviser => adviser.Crd);

    [Fact]
    public void BareWorkbook_ReadsTheLaterLayout()
    {
        using var download = File.OpenRead(Asset("ria-sample-2023-06.xlsx"));

        var advisers = Parse(download, "https://www.sec.gov/files/ia060123.xlsx");

        var bny = advisers[231];
        bny.FirmType.Should().Be("Registered");
        bny.Cik.Should().Be("277871");
        bny.TotalRegulatoryAum.Should().Be(5_275_585_428L);
        bny.LatestFilingDate.Should().Be(new DateOnly(2023, 4, 25));
        bny.PrivateFundCount.Should().Be(0);
        bny.ClientTypes.Should().HaveCount(6);
        advisers[105958].Cik.Should().Be("102909");
        advisers.Values.Should().Contain(adviser => adviser.HedgeFundCount >= 3);
    }

    [Fact]
    public void WorkbookInsideAZip_ReadsTheEarlyLayoutWithoutItsMissingColumns()
    {
        using var download = ZipOf(Asset("ria-sample-2019-12.xlsx"), "IA_ADV_Report.xlsx");

        var advisers = Parse(download, "https://www.sec.gov/files/ia120219.zip");

        var bny = advisers[231];
        bny.TotalRegulatoryAum.Should().Be(1_482_940_388L);
        bny.LatestFilingDate.Should().Be(new DateOnly(2019, 10, 23));
        bny.AdvisesPrivateFunds.Should().BeFalse();
        bny.Cik.Should().BeNull("the 2019 layout has no CIK column");
        bny.PrivateFundCount.Should().BeNull("the 2019 layout has no fund counts");
        bny.ClientTypes.Should().HaveCount(6);
        advisers.Should().ContainKey(105958);
    }

    [Fact]
    public void ZipWithoutARoster_IsNull()
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            archive.CreateEntry("notice.pdf");
        buffer.Position = 0;

        FormAdvRosterFile.Open(buffer, "https://www.sec.gov/files/ia.zip").Should().BeNull();
    }

    [Theory]
    [InlineData("10/23/2019")]
    [InlineData("43761")]
    public void FilingDate_ReadsTextOrAnExcelSerial(string cell)
    {
        var csv = $"Organization CRD#,Firm Type,Latest ADV Filing Date\n231,Registered,{cell}\n";

        var adviser = FormAdvCsvParser.Parse(new StringReader(csv)).Single();

        adviser.LatestFilingDate.Should().Be(new DateOnly(2019, 10, 23));
    }

    private static MemoryStream ZipOf(string path, string entryName)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry(entryName).Open();
            using var source = File.OpenRead(path);
            source.CopyTo(entry);
        }
        buffer.Position = 0;
        return buffer;
    }
}
