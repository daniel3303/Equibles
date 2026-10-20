using System.Text.RegularExpressions;

namespace Equibles.UnitTests.Holdings;

// InstitutionalHolder.Classification was filled by matching words in the filer's name. Holder types
// now come only from regulatory evidence (InstitutionalHolderProfile); the column stays mapped until
// it is dropped, so nothing outside the entity and its migrations may read, write or compute it.
public class HolderNameClassificationRetirementTests
{
    private static readonly Regex[] Forbidden =
    [
        new(@"\bFundClassification\b"),
        new(@"\bFundClassifier\w*"),
        new(@"\.Classification\b"),
        new(@"(?<![\w.])Classification\s*=(?!=)"),
    ];

    private static readonly HashSet<string> MappedOnly =
    [
        "src/Equibles.Holdings.Data/Models/InstitutionalHolder.cs",
        "src/Equibles.Holdings.Data/Models/FundClassification.cs",
        "tests/Equibles.UnitTests/Holdings/HolderNameClassificationRetirementTests.cs",
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
        var root = RepositoryRoot();
        var offenders = new[] { "src", "tests" }
            .SelectMany(folder => SourceFiles(new DirectoryInfo(Path.Combine(root, folder))))
            .Select(file =>
                (File: file, Path: Path.GetRelativePath(root, file.FullName).Replace('\\', '/'))
            )
            .Where(item => !MappedOnly.Contains(item.Path))
            .SelectMany(item =>
                File.ReadLines(item.File.FullName)
                    .Select((line, index) => (item.Path, Line: line, Number: index + 1))
            )
            .Where(item => Forbidden.Any(pattern => pattern.IsMatch(item.Line)))
            .Select(item => $"{item.Path}:{item.Number}: {item.Line.Trim()}")
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "holder types come from InstitutionalHolderProfile, never from the filer's name"
            );
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
        ];

        uses.Should().OnlyContain(line => Forbidden.Any(pattern => pattern.IsMatch(line)));
        Forbidden
            .Any(pattern =>
                pattern.IsMatch("/// SEC's 4-digit Standard Industrial Classification code")
            )
            .Should()
            .BeFalse();
    }

    private static IEnumerable<FileInfo> SourceFiles(DirectoryInfo directory)
    {
        foreach (var file in directory.EnumerateFiles())
        {
            if (file.Extension is ".cs" or ".cshtml")
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
