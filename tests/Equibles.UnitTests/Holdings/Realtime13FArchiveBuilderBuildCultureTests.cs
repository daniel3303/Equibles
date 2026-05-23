using System.Globalization;
using System.IO.Compression;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.UnitTests.Holdings;

/// <summary>
/// Build emits TSV matching SEC's quarterly data-set format, which uses
/// Gregorian ISO dates. ToString("yyyy-MM-dd") without InvariantCulture
/// produces Hijri calendar dates on ar-SA threads, corrupting the import
/// pipeline downstream.
/// </summary>
public class Realtime13FArchiveBuilderBuildCultureTests
{
    [Fact]
    public void Build_HijriCultureThread_EmitsGregorianIsoDates()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");

            var filing = new Parsed13FFiling
            {
                AccessionNumber = "0000000000-24-000001",
                Cik = "1234567",
                FilingDate = new DateOnly(2024, 3, 15),
                PeriodOfReport = new DateOnly(2024, 3, 31),
                FilingManagerName = "TEST FUND",
                City = "NEW YORK",
                StateOrCountry = "NY",
            };

            using var archive = new Realtime13FArchiveBuilder().Build([filing]);

            var entry = archive.GetEntry("SUBMISSION.tsv");
            using var reader = new StreamReader(entry.Open());
            var tsv = reader.ReadToEnd();

            tsv.Should().Contain("2024-03-15", "FilingDate must be Gregorian ISO, not Hijri");
            tsv.Should().Contain("2024-03-31", "PeriodOfReport must be Gregorian ISO, not Hijri");
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }
}
