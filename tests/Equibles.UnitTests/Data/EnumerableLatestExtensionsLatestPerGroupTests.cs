using Equibles.Data.Extensions;

namespace Equibles.UnitTests.Data;

public class EnumerableLatestExtensionsLatestPerGroupTests
{
    private sealed record Row(string Key, DateOnly Date, string Tag);

    [Fact]
    public void LatestPerGroup_MultipleRowsPerKey_ReturnsHighestDatePerGroup()
    {
        // Contract: group by key and return the single row with the HIGHEST date per group.
        // Place each group's newest row mid-sequence so an ascending-sort or first-by-position
        // bug would pick the wrong row. Zero existing tests for this member.
        var rows = new[]
        {
            new Row("A", new DateOnly(2024, 1, 1), "a-old"),
            new Row("A", new DateOnly(2024, 3, 1), "a-new"),
            new Row("A", new DateOnly(2024, 2, 1), "a-mid"),
            new Row("B", new DateOnly(2024, 5, 1), "b-new"),
            new Row("B", new DateOnly(2024, 4, 1), "b-old"),
        };

        var latest = rows.LatestPerGroup(r => r.Key, r => r.Date).ToList();

        latest.Should().HaveCount(2);
        latest.Should().ContainSingle(r => r.Key == "A").Which.Tag.Should().Be("a-new");
        latest.Should().ContainSingle(r => r.Key == "B").Which.Tag.Should().Be("b-new");
    }
}
