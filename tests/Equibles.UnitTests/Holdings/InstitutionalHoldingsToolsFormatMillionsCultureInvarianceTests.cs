using System.Globalization;
using System.Reflection;
using Equibles.Holdings.Mcp.Tools;

namespace Equibles.UnitTests.Holdings;

public class InstitutionalHoldingsToolsFormatMillionsCultureInvarianceTests
{
    private static readonly MethodInfo FormatMillionsMethod =
        typeof(InstitutionalHoldingsTools).GetMethod(
            "FormatMillions",
            BindingFlags.NonPublic | BindingFlags.Static
        );

    private static string Invoke(decimal value) =>
        (string)FormatMillionsMethod.Invoke(null, [value]);

    // FormatMillions renders raw dollar values in $millions (N1) and feeds the
    // value cells across the institutional-holdings MCP tables. Every numeric
    // cell in this file is invariant by contract — MCP markdown is consumed by
    // LLMs trained on en-US conventions, so a host CurrentCulture (de-DE swaps
    // group/decimal separators to 1.234,5) must not fork the output. Attacks
    // the culture risk surface: a value carrying both a thousands and a decimal
    // separator must render US-style regardless of CurrentCulture.
    [Fact]
    public void FormatMillions_UnderNonInvariantCulture_RendersInvariantSeparators()
    {
        var original = CultureInfo.CurrentCulture;
        string invariantOutput;
        string deDeOutput;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            invariantOutput = Invoke(1_234_500_000m);

            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            deDeOutput = Invoke(1_234_500_000m);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        invariantOutput.Should().Be("1,234.5");
        deDeOutput
            .Should()
            .Be(
                invariantOutput,
                "FormatMillions must thread InvariantCulture so MCP markdown does not fork its group/decimal separators by host locale (de-DE would otherwise render 1.234,5)"
            );
    }
}
