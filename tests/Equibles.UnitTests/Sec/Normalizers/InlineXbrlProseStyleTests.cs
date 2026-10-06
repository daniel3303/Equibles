using static Equibles.UnitTests.Sec.Normalizers.InlineXbrlProseTestSource;

namespace Equibles.UnitTests.Sec.Normalizers;

public class InlineXbrlProseStyleTests
{
    [Theory]
    [InlineData("display:none")]
    [InlineData("visibility:hidden")]
    [InlineData("opacity:0")]
    [InlineData("content-visibility:hidden")]
    [InlineData("text-decoration:line-through")]
    [InlineData("text-transform:uppercase")]
    [InlineData("clip-path:inset(100%)")]
    [InlineData("display:var(--hidden)")]
    [InlineData("height:0;overflow:hidden")]
    [InlineData("transform:scale(0)")]
    [InlineData("-webkit-transform:scale(0)")]
    [InlineData("font-size:0%")]
    [InlineData("color:rgba(0,0,0,0)")]
    [InlineData("-webkit-text-fill-color:transparent")]
    [InlineData("transform:translateX(1em)")]
    [InlineData("mask-image:linear-gradient(transparent,transparent)")]
    [InlineData("letter-spacing:-1000px")]
    public void InlineOrStylesheetHiddenContentCannotMoveOutOfItsScope(string style)
    {
        foreach (var name in new[] { "p", "div", "ix:continuation" })
        {
            var source = Source()
                .Replace(
                    "<" + name,
                    "<" + name + " style='" + style + "'",
                    StringComparison.Ordinal
                );
            Convert(source).QuerySelectorAll("p").Should().HaveCount(2);
            source = Source()
                .Replace(
                    "<body>",
                    $"<head><style>.hidden {{{style}}}</style></head><body>",
                    StringComparison.Ordinal
                )
                .Replace("<" + name, "<" + name + " class='hidden'", StringComparison.Ordinal);
            Convert(source).QuerySelectorAll("p").Should().HaveCount(2);
        }
    }

    [Theory]
    [InlineData("<style>@media screen {.hidden{display:none}}</style>")]
    [InlineData("<style>@import url(https://example.com/style.css);</style>")]
    [InlineData("<style>@supports (display:grid) {.hidden{display:none}}</style>")]
    [InlineData("<link rel='stylesheet' href='https://example.com/style.css'>")]
    [InlineData("<style media='(max-width:500px)'>p{display:none}</style>")]
    [InlineData("<style scoped>p{display:none}</style>")]
    [InlineData("<style type='application/unknown'>p{display:none}</style>")]
    [InlineData("<style>p::before{content:'No longer applicable: '}</style>")]
    [InlineData("<style>p::first-line{color:transparent}</style>")]
    public void UnknownOrConditionalStylesPreserveBoundaries(string head)
    {
        Convert(
                Source()
                    .Replace("<body>", "<head>" + head + "</head><body>", StringComparison.Ordinal)
            )
            .QuerySelectorAll("p")
            .Should()
            .HaveCount(2);
    }

    [Fact]
    public void UnrelatedHiddenSelectorsDoNotBlockVisibleProse()
    {
        var source = Source()
            .Replace(
                "<body>",
                "<head><style>.other{display:none} p{color:black}</style></head><body>",
                StringComparison.Ordinal
            );
        Convert(source).QuerySelectorAll("p").Should().ContainSingle();
    }

    [Theory]
    [InlineData(".hidden{display:none}.hidden{display:block}")]
    [InlineData(".\\68 idden{display:none}")]
    public void OverridesAndEscapedSelectorsDoNotMakeHiddenContentSafe(string css)
    {
        var source = Source()
            .Replace("<body>", $"<head><style>{css}</style></head><body>", StringComparison.Ordinal)
            .Replace("<p>", "<p class='hidden'>", StringComparison.Ordinal);
        Convert(source).QuerySelectorAll("p").Should().HaveCount(2);
    }
}
