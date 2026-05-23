using System.IO.Compression;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

// Lane A (adversarial): the Clean method's contract states "The TSV reader
// splits on tabs and newlines; any of those embedded in a free-text field
// would corrupt the whole row. Replace them with spaces." A regression that
// drops Clean or weakens it to handle only one character class would let
// an embedded tab split the manager name across two TSV columns, silently
// shifting every downstream field and corrupting the import.
public class Realtime13FArchiveBuilderCleanSanitizationTests
{
    private readonly Realtime13FArchiveBuilder _sut = new();

    [Fact]
    public void Build_ManagerNameContainsTabAndNewline_SanitizesToSpacesInOutputTsv()
    {
        var filing = new Parsed13FFiling
        {
            AccessionNumber = "0001234567-24-000001",
            Cik = "12345",
            FilingDate = new DateOnly(2024, 6, 1),
            PeriodOfReport = new DateOnly(2024, 3, 31),
            IsAmendment = false,
            FilingManagerName = "ACME\tFund\nManagement\rLLC",
            City = "NEW\tYORK",
            StateOrCountry = "NY",
        };

        using var archive = _sut.Build([filing]);

        var coverPage = ReadEntry(archive, "COVERPAGE.tsv");
        var dataLines = coverPage.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var dataRow = dataLines.Length > 1 ? dataLines[1] : "";
        var fields = dataRow.Split('\t');

        fields.Should().HaveCount(7, "a clean row has exactly 7 TSV columns");
        fields[2].Should().Be("ACME Fund Management LLC");
        fields[3].Should().Be("NEW YORK");
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name);
        using var stream = entry.Open();
        using var reader = new System.IO.StreamReader(stream);
        return reader.ReadToEnd();
    }
}
