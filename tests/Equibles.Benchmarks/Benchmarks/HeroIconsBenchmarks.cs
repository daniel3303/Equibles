using BenchmarkDotNet.Attributes;
using Equibles.Web.TagHelpers;

namespace Equibles.Benchmarks.Benchmarks;

/// <summary>
/// Per-call cost of <see cref="HeroIcons.Render"/>. Every <c>&lt;icon&gt;</c> tag in every page
/// render — header, nav, buttons, tables, cards — hits this method, so its allocation profile
/// scales with view complexity. Renders a representative mix of icons in both outline and solid
/// styles to keep the dictionary-lookup + string-interpolation path realistic.
/// </summary>
[MemoryDiagnoser]
public class HeroIconsBenchmarks
{
    // A header/nav-flavoured set: a generic chart, a search icon, a chevron, a presence indicator,
    // and a missing-icon case (which exercises the fallback path). The list intentionally mixes
    // outline (default for nav) and solid (used in active states/buttons).
    private static readonly (string Name, HeroIcons.IconStyle Style)[] IconMix =
    [
        ("chart-bar", HeroIcons.IconStyle.Outline),
        ("plus", HeroIcons.IconStyle.Outline),
        ("plus", HeroIcons.IconStyle.Solid),
        ("plus-circle", HeroIcons.IconStyle.Outline),
        ("circle-stack", HeroIcons.IconStyle.Outline),
        ("funnel", HeroIcons.IconStyle.Outline),
        ("definitely-not-a-real-icon", HeroIcons.IconStyle.Outline), // fallback path
    ];

    [Benchmark]
    public int RenderMixedIconSet()
    {
        var total = 0;
        for (var i = 0; i < IconMix.Length; i++)
        {
            var (name, style) = IconMix[i];
            total += HeroIcons.Render(name, style, "6", null).Length;
        }
        return total;
    }
}
