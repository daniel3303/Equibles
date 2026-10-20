using System.Text.RegularExpressions;

namespace Equibles.UnitTests.Holdings.Ownership;

/// <summary>
/// Both holdings tables hold 13F and Schedule 13D/G rows, so every holdings load behind the
/// unified holder list must name its filing type, and nesting reads only the confirmed rollups.
/// </summary>
public class OwnershipLoadConventionTests
{
    private static readonly Regex HoldingRepositoryCall = new(
        @"_holdingRepository\s*\.\s*(\w+)\s*\(",
        RegexOptions.Compiled
    );

    // Reads of tables other than the holding rows (snapshot state, 13D/G reporting persons).
    private static readonly string[] NotHoldingRowReads =
    [
        "GetMarketActivitySnapshotState",
        "GetBeneficialOwnershipPersons",
    ];

    [Fact]
    public void OwnershipHoldingLoads_NameTheirFilingType()
    {
        var repository = File.ReadAllText(
            Path.Combine(
                RepositoryRoot(),
                "src",
                "Equibles.Holdings.Repositories",
                "InstitutionalHoldingRepository.cs"
            )
        );
        var calls = OwnershipSources()
            .SelectMany(source => HoldingRepositoryCall.Matches(source.Text))
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .Except(NotHoldingRowReads)
            .ToList();

        calls.Should().NotBeEmpty();
        foreach (var method in calls)
        {
            var body = MethodBody(repository, method);
            var namesFilingType =
                body.Contains("Is13F")
                || body.Contains("FilingType.Form13F")
                || (
                    body.Contains("FilingType.Schedule13D")
                    && body.Contains("FilingType.Schedule13G")
                )
                || Regex.IsMatch(body, @"\b(Get13F|GetSchedule13DG)\w*\(");
            namesFilingType
                .Should()
                .BeTrue(
                    $"{method} must load only Form 13F rows or the Schedule 13D/G pair, never both tables' mixed rows"
                );
        }
    }

    [Fact]
    public void OwnershipCode_NeverLoadsHoldingRowsDirectly()
    {
        foreach (var (path, text) in OwnershipSources())
        {
            text.Should()
                .NotMatchRegex(
                    @"Set<Institutional(Holding|Filing)>",
                    $"{path} must read holdings through the filing-typed repository seams"
                );
            text.Should()
                .NotMatchRegex(
                    @"_holdingRepository\s*\.\s*(GetByStock|GetHistoryByStock|GetAll)\s*\(",
                    $"{path} must never read the unfiltered per-stock holdings"
                );
        }
    }

    [Fact]
    public void Nesting_ReadsOnlyTheConfirmedRollups()
    {
        // A third party's 13F can list another manager's file number (Russell lists BlackRock,
        // Inc.'s), so the list must never read other-manager lines or raw link evidence; the
        // rollup already keeps only containment the child confirms.
        foreach (var (path, text) in OwnershipSources())
            text.Should()
                .NotMatchRegex(
                    @"\b(FilingOtherManager|HolderLinkEvidence|Form13FNotice|GetContainedChildren)\b",
                    $"{path} must nest by the stored rollups only"
                );
    }

    private static IEnumerable<(string Path, string Text)> OwnershipSources() =>
        Directory
            .EnumerateFiles(
                Path.Combine(
                    RepositoryRoot(),
                    "src",
                    "Equibles.Holdings.BusinessLogic",
                    "Ownership"
                ),
                "*.cs",
                SearchOption.AllDirectories
            )
            .Select(path => (path, File.ReadAllText(path)));

    // The method's text up to the next member declared at class level.
    private static string MethodBody(string source, string method)
    {
        var start = Regex.Match(source, $@"\n    public [^\n]*\b{method}\s*\(");
        start.Success.Should().BeTrue($"{method} is declared on InstitutionalHoldingRepository");
        var next = Regex.Match(
            source[(start.Index + start.Length)..],
            @"\n    (public|private|internal|protected) "
        );
        var end = next.Success ? start.Index + start.Length + next.Index : source.Length;
        return source[start.Index..end];
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
