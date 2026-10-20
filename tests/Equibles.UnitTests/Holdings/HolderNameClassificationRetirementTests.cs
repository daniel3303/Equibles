using System.Text.RegularExpressions;

namespace Equibles.UnitTests.Holdings;

// InstitutionalHolder.Classification was filled by matching words in the filer's name. Holder types
// now come only from regulatory evidence (InstitutionalHolderProfile); the column stays mapped until
// it is dropped, so only its mapping lines may name it, and no code may read, write or compute it.
public class HolderNameClassificationRetirementTests
{
    private const string ThisFile =
        "tests/Equibles.UnitTests/Holdings/HolderNameClassificationRetirementTests.cs";

    private static readonly Regex[] Forbidden =
    [
        new(@"\bFundClassification\b"),
        new(@"\bFundClassifier\w*"),
        new(@"\.Classification\b"),
        new(@"(?<![\w.])Classification\s*=(?!=)"),
        new(@"(?<![\w.])Classification\s*:"),
        new(@"\\?""Classification\\?"""),
        new(@"EF\.Property<[^>]*>\([^)]*""Classification"""),
        new(@"nameof\((?:\w+\.)*Classification\)"),
    ];

    // The entity and enum still map the column; these exact lines are the only allowed mentions.
    private static readonly HashSet<(string Path, string Line)> MappingLines =
    [
        (
            "src/Equibles.Holdings.Data/Models/InstitutionalHolder.cs",
            "[Index(nameof(Classification))]"
        ),
        (
            "src/Equibles.Holdings.Data/Models/InstitutionalHolder.cs",
            "public FundClassification Classification { get; set; }"
        ),
        (
            "src/Equibles.Holdings.Data/Models/FundClassification.cs",
            "public enum FundClassification"
        ),
    ];

    private static readonly HashSet<string> SkippedDirectories =
    [
        "bin",
        "obj",
        "node_modules",
        "Equibles.Migrations",
    ];

    [Fact]
    public void No_code_reads_or_writes_the_name_derived_holder_classification()
    {
        var offenders = ScannedLines()
            .Where(item => !MappingLines.Contains((item.Path, item.Line.Trim())))
            .Where(item => Forbidden.Any(pattern => pattern.IsMatch(item.Line)))
            .Select(item => $"{item.Path}:{item.Number}: {item.Line.Trim()}")
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "holder types come from InstitutionalHolderProfile, never from the filer's name"
            );
    }

    // Guards the scan itself: an empty file set or a stale mapping line would let it pass on nothing.
    [Fact]
    public void The_scan_reads_the_holdings_importer_and_every_mapping_line()
    {
        var lines = ScannedLines().ToList();

        lines
            .Select(item => item.Path)
            .Should()
            .Contain("src/Equibles.Holdings.HostedService/Services/HoldingsImportService.cs");
        lines
            .Select(item => (item.Path, item.Line.Trim()))
            .Should()
            .Contain(MappingLines, "an allowed mapping line that no longer exists must be removed");
    }

    [Fact]
    public void The_scan_flags_every_kind_of_classification_use()
    {
        string[] uses =
        [
            "holder.Classification = result;",
            "Classification = FundClassifier.Classify(name),",
            ".Where(h => h.Classification == FundClassification.Unknown)",
            "var label = FundClassification.HedgeFund;",
            "WHERE \"Classification\" <> 0",
            "\"UPDATE \\\"InstitutionalHolder\\\" SET \\\"Classification\\\" = 0\"",
            ".Where(h => EF.Property<int>(h, \"Classification\") > 0)",
            "if (holder is { Classification: 5 })",
            "[Index(nameof(InstitutionalHolder.Classification))]",
        ];

        uses.Should().OnlyContain(line => Forbidden.Any(pattern => pattern.IsMatch(line)));
        Forbidden
            .Any(pattern =>
                pattern.IsMatch("/// SEC's 4-digit Standard Industrial Classification code")
            )
            .Should()
            .BeFalse();
    }

    private static IEnumerable<(string Path, string Line, int Number)> ScannedLines()
    {
        var root = RepositoryRoot();
        return new[] { "src", "tests", "scripts" }
            .Select(folder => new DirectoryInfo(Path.Combine(root, folder)))
            .Where(directory => directory.Exists)
            .SelectMany(SourceFiles)
            .Select(file =>
                (File: file, Path: Path.GetRelativePath(root, file.FullName).Replace('\\', '/'))
            )
            .Where(item => item.Path != ThisFile)
            .SelectMany(item =>
                File.ReadLines(item.File.FullName)
                    .Select((line, index) => (item.Path, Line: line, Number: index + 1))
            );
    }

    private static IEnumerable<FileInfo> SourceFiles(DirectoryInfo directory)
    {
        foreach (var file in directory.EnumerateFiles())
        {
            if (file.Extension is ".cs" or ".cshtml" or ".sql")
                yield return file;
        }

        foreach (var child in directory.EnumerateDirectories())
        {
            if (SkippedDirectories.Contains(child.Name))
                continue;
            foreach (var file in SourceFiles(child))
                yield return file;
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Equibles.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
