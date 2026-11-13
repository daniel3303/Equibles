using System.Text.Json;
using Equibles.Mcp;
using ModelContextProtocol.Protocol;

namespace Equibles.UnitTests.Mcp;

public class InvalidParamsTranslatingToolIgnoredArgumentsTests
{
    private static readonly Tool DividendTool = new()
    {
        Name = "GetDividendHistory",
        InputSchema = JsonDocument
            .Parse("""{"type":"object","properties":{"ticker":{},"maxResults":{}}}""")
            .RootElement,
    };

    private static CallToolResult Answer() =>
        new() { Content = [new TextContentBlock { Text = "rows" }] };

    private static Dictionary<string, JsonElement> Arguments(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

    [Fact]
    public void NoteIgnoredArguments_UndeclaredOrMiscasedName_IsNamedInTheAnswer()
    {
        var result = Answer();

        InvalidParamsTranslatingTool.NoteIgnoredArguments(
            result,
            DividendTool,
            Arguments("""{"ticker":"T","limit":5,"MaxResults":1}""")
        );

        result
            .Content.OfType<TextContentBlock>()
            .Single()
            .Text.Should()
            .Be(
                "rows\n\nNote: GetDividendHistory ignored the arguments MaxResults, limit; its arguments are ticker, maxResults."
            );
    }

    [Fact]
    public void NoteIgnoredArguments_OnlyDeclaredArguments_LeavesTheAnswerAlone()
    {
        var result = Answer();

        InvalidParamsTranslatingTool.NoteIgnoredArguments(
            result,
            DividendTool,
            Arguments("""{"ticker":"T","maxResults":5}""")
        );

        result.Content.OfType<TextContentBlock>().Single().Text.Should().Be("rows");
    }
}
