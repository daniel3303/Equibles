using System.IO.Compression;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.HostedService.Services;

namespace Equibles.Tests.Holdings;

public class HoldingsImportServiceTests {
    // ── TryParseDateOnly ──

    [Theory]
    [InlineData("2024-03-15", 2024, 3, 15)]
    [InlineData("2019-12-31", 2019, 12, 31)]
    [InlineData("2000-01-01", 2000, 1, 1)]
    public void TryParseDateOnly_IsoFormat_ParsesCorrectly(string input, int year, int month, int day) {
        var success = HoldingsImportService.TryParseDateOnly(input, out var result);

        success.Should().BeTrue();
        result.Should().Be(new DateOnly(year, month, day));
    }

    [Theory]
    [InlineData("31-DEC-2019", 2019, 12, 31)]
    [InlineData("01-JAN-2020", 2020, 1, 1)]
    [InlineData("15-MAR-2024", 2024, 3, 15)]
    public void TryParseDateOnly_SecFormat_ParsesCorrectly(string input, int year, int month, int day) {
        var success = HoldingsImportService.TryParseDateOnly(input, out var result);

        success.Should().BeTrue();
        result.Should().Be(new DateOnly(year, month, day));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-date")]
    [InlineData("xyz-abc-1234")]
    public void TryParseDateOnly_InvalidInput_ReturnsFalse(string input) {
        var success = HoldingsImportService.TryParseDateOnly(input, out _);

        success.Should().BeFalse();
    }

    // ── ParseShareType ──

    [Theory]
    [InlineData("SH", ShareType.Shares)]
    [InlineData("sh", ShareType.Shares)]
    [InlineData("PRN", ShareType.Principal)]
    [InlineData("prn", ShareType.Principal)]
    public void ParseShareType_ValidInput_ReturnsCorrectType(string input, ShareType expected) {
        HoldingsImportService.ParseShareType(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UNKNOWN")]
    public void ParseShareType_InvalidOrNull_DefaultsToShares(string input) {
        HoldingsImportService.ParseShareType(input).Should().Be(ShareType.Shares);
    }

    // ── ParseOptionType ──

    [Theory]
    [InlineData("PUT", OptionType.Put)]
    [InlineData("put", OptionType.Put)]
    [InlineData("CALL", OptionType.Call)]
    [InlineData("call", OptionType.Call)]
    public void ParseOptionType_ValidInput_ReturnsCorrectType(string input, OptionType expected) {
        HoldingsImportService.ParseOptionType(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UNKNOWN")]
    public void ParseOptionType_InvalidOrNull_ReturnsNull(string input) {
        HoldingsImportService.ParseOptionType(input).Should().BeNull();
    }

    // ── ParseInvestmentDiscretion ──

    [Theory]
    [InlineData("SOLE", InvestmentDiscretion.Sole)]
    [InlineData("sole", InvestmentDiscretion.Sole)]
    [InlineData("DFND", InvestmentDiscretion.Defined)]
    [InlineData("OTR", InvestmentDiscretion.Other)]
    public void ParseInvestmentDiscretion_ValidInput_ReturnsCorrectValue(string input, InvestmentDiscretion expected) {
        HoldingsImportService.ParseInvestmentDiscretion(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("UNKNOWN")]
    public void ParseInvestmentDiscretion_InvalidOrNull_DefaultsToSole(string input) {
        HoldingsImportService.ParseInvestmentDiscretion(input).Should().Be(InvestmentDiscretion.Sole);
    }

    // ── ParseLong ──

    [Theory]
    [InlineData("42", 42)]
    [InlineData("0", 0)]
    [InlineData("-100", -100)]
    [InlineData("1000000", 1_000_000)]
    public void ParseLong_ValidInput_ReturnsValue(string input, long expected) {
        HoldingsImportService.ParseLong(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public void ParseLong_InvalidOrNull_ReturnsZero(string input) {
        HoldingsImportService.ParseLong(input).Should().Be(0);
    }

    // ── ParseNullableInt ──

    [Fact]
    public void ParseNullableInt_ValidInput_ReturnsValue() {
        HoldingsImportService.ParseNullableInt("42").Should().Be(42);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    public void ParseNullableInt_InvalidOrNull_ReturnsNull(string input) {
        HoldingsImportService.ParseNullableInt(input).Should().BeNull();
    }

    // ── GetValue ──

    [Fact]
    public void GetValue_KeyExists_ReturnsValue() {
        var row = new Dictionary<string, string> { ["NAME"] = "Test" };

        HoldingsImportService.GetValue(row, "NAME").Should().Be("Test");
    }

    [Fact]
    public void GetValue_KeyMissing_ReturnsNull() {
        var row = new Dictionary<string, string>();

        HoldingsImportService.GetValue(row, "MISSING").Should().BeNull();
    }

    // ── FindEntry ──

    [Fact]
    public void FindEntry_FlatArchive_FindsByName() {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true)) {
            archive.CreateEntry("SUBMISSION.tsv");
        }

        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);

        var entry = HoldingsImportService.FindEntry(readArchive, "SUBMISSION.tsv");
        entry.Should().NotBeNull();
        entry.Name.Should().Be("SUBMISSION.tsv");
    }

    [Fact]
    public void FindEntry_NestedArchive_FindsByFileName() {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true)) {
            archive.CreateEntry("subfolder/SUBMISSION.tsv");
        }

        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);

        var entry = HoldingsImportService.FindEntry(readArchive, "SUBMISSION.tsv");
        entry.Should().NotBeNull();
        entry.Name.Should().Be("SUBMISSION.tsv");
    }

    [Fact]
    public void FindEntry_MissingFile_ReturnsNull() {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true)) {
            archive.CreateEntry("OTHER.tsv");
        }

        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);

        HoldingsImportService.FindEntry(readArchive, "SUBMISSION.tsv").Should().BeNull();
    }

    // ── ResolveManagerName ──

    [Fact]
    public void ResolveManagerName_NullManagerNumber_ReturnsNull() {
        var context = new ImportContext {
            OtherManagers = new Dictionary<string, Dictionary<int, string>>()
        };

        HoldingsImportService.ResolveManagerName(context, "ACC-001", null).Should().BeNull();
    }

    [Fact]
    public void ResolveManagerName_FoundInMapping_ReturnsName() {
        var context = new ImportContext {
            OtherManagers = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase) {
                ["ACC-001"] = new() { [1] = "Goldman Sachs" }
            }
        };

        HoldingsImportService.ResolveManagerName(context, "ACC-001", 1).Should().Be("Goldman Sachs");
    }

    [Fact]
    public void ResolveManagerName_AccessionMissing_ReturnsNull() {
        var context = new ImportContext {
            OtherManagers = new Dictionary<string, Dictionary<int, string>>()
        };

        HoldingsImportService.ResolveManagerName(context, "ACC-999", 1).Should().BeNull();
    }

    [Fact]
    public void ResolveManagerName_SequenceNumberMissing_ReturnsNull() {
        var context = new ImportContext {
            OtherManagers = new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase) {
                ["ACC-001"] = new() { [1] = "Goldman Sachs" }
            }
        };

        HoldingsImportService.ResolveManagerName(context, "ACC-001", 99).Should().BeNull();
    }

    // ── DeduplicateSubmissions ──

    [Fact]
    public void DeduplicateSubmissions_NoDuplicates_KeepsAll() {
        var context = new ImportContext {
            Submissions = new Dictionary<string, SubmissionRow>(StringComparer.OrdinalIgnoreCase) {
                ["ACC-001"] = new() { AccessionNumber = "ACC-001", Cik = "CIK1", PeriodOfReport = "2024-01-01", FilingDate = "2024-01-15" },
                ["ACC-002"] = new() { AccessionNumber = "ACC-002", Cik = "CIK2", PeriodOfReport = "2024-01-01", FilingDate = "2024-01-15" },
            }
        };

        HoldingsImportService.DeduplicateSubmissions(context);

        context.Submissions.Should().HaveCount(2);
    }

    [Fact]
    public void DeduplicateSubmissions_DuplicateCikAndPeriod_KeepsLatestByFilingDate() {
        var context = new ImportContext {
            Submissions = new Dictionary<string, SubmissionRow>(StringComparer.OrdinalIgnoreCase) {
                ["ACC-001"] = new() { AccessionNumber = "ACC-001", Cik = "CIK1", PeriodOfReport = "2024-03-31", FilingDate = "2024-04-01" },
                ["ACC-002"] = new() { AccessionNumber = "ACC-002", Cik = "CIK1", PeriodOfReport = "2024-03-31", FilingDate = "2024-05-01" },
                ["ACC-003"] = new() { AccessionNumber = "ACC-003", Cik = "CIK2", PeriodOfReport = "2024-03-31", FilingDate = "2024-04-01" },
            }
        };

        HoldingsImportService.DeduplicateSubmissions(context);

        context.Submissions.Should().HaveCount(2);
        context.Submissions.Should().ContainKey("ACC-002");
        context.Submissions.Should().ContainKey("ACC-003");
        context.Submissions.Should().NotContainKey("ACC-001");
    }

    [Fact]
    public void DeduplicateSubmissions_MissingCikOrPeriod_SkippedFromGrouping() {
        var context = new ImportContext {
            Submissions = new Dictionary<string, SubmissionRow>(StringComparer.OrdinalIgnoreCase) {
                ["ACC-001"] = new() { AccessionNumber = "ACC-001", Cik = null, PeriodOfReport = "2024-01-01", FilingDate = "2024-01-01" },
                ["ACC-002"] = new() { AccessionNumber = "ACC-002", Cik = "CIK1", PeriodOfReport = null, FilingDate = "2024-01-01" },
            }
        };

        HoldingsImportService.DeduplicateSubmissions(context);

        context.Submissions.Should().HaveCount(2);
    }
}
