using System.Reflection;
using Equibles.GovernmentContracts.Mcp.Tools;

namespace Equibles.UnitTests.GovernmentContracts;

public class GovernmentContractsToolsShortenSurrogateTests
{
    // Contract: Shorten caps a display string at maxLength for an MCP markdown
    // cell (GetGovernmentContracts truncates each contract Description to 80).
    // Truncation must leave the result well-formed UTF-16 — the sibling
    // UsaSpendingAwardMapper.Truncate in this same module was fixed for exactly
    // this (GH-3786): slicing through a surrogate pair orphans a lone surrogate,
    // which is invalid UTF-16 and corrupts JSON serialization of the tool reply.
    // Here the cut lands between the two halves of "😀" (U+1F600), so a raw
    // value[..80] keeps the high half and drops the low half.
    //
    // Reflection-invoke since Shorten is private static.
    [Fact(Skip = "GH-3827 — Shorten orphans a surrogate pair when truncating")]
    public void Shorten_CutThroughSurrogatePair_DoesNotOrphanSurrogate()
    {
        var method = typeof(GovernmentContractsTools).GetMethod(
            "Shorten",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        // 79 BMP chars place the high surrogate of "😀" at index 79, so a raw
        // value[..80] retains the high half and orphans it.
        var input = new string('a', 79) + "😀" + new string('b', 10);

        var result = (string)method!.Invoke(null, [input, 80]);

        HasUnpairedSurrogate(result)
            .Should()
            .BeFalse("truncation must not split a surrogate pair into a lone surrogate");
    }

    private static bool HasUnpairedSurrogate(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                    return true;
                i++;
            }
            else if (char.IsLowSurrogate(value[i]))
            {
                return true;
            }
        }

        return false;
    }
}
