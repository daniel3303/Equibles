using Equibles.Sec.HostedService.Services;

namespace Equibles.UnitTests.Sec;

public class FtdImportServiceListedStrippedAliasesTests
{
    [Fact]
    public void BuildListedStrippedAliases_GivesSecondaryLinesNoStrippedSpelling()
    {
        var aliases = FtdImportService.BuildListedStrippedAliases(
            ["BRK-B", "INN-PF"],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "INN-PF" }
        );

        // INNPF is another company's symbol in the same fails-to-deliver file.
        aliases.Should().ContainKey("BRKB").WhoseValue.Should().Be("BRK-B");
        aliases.Should().NotContainKey("INNPF");
    }
}
