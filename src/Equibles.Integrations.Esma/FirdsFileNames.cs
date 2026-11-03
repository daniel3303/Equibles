using System.Globalization;
using System.Text.RegularExpressions;
using Equibles.Integrations.Esma.Models;

namespace Equibles.Integrations.Esma;

public static partial class FirdsFileNames
{
    [GeneratedRegex(@"^(FULINS_[EC]|DLTINS)_(\d{8})_(\d{2})of(\d{2})\.zip$")]
    private static partial Regex Pattern();

    // CFI category E (equities, exchange-traded commodities and notes) and C (collective investment vehicles, where
    // exchange-traded funds sit) full files and the deltas matter here; the index lists every asset class.
    public static bool TryParse(string fileName, out FirdsFileType type, out DateOnly publishedOn)
    {
        type = default;
        publishedOn = default;
        if (fileName == null)
            return false;
        var match = Pattern().Match(fileName);
        if (
            !match.Success
            || !DateOnly.TryParseExact(
                match.Groups[2].Value,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out publishedOn
            )
        )
            return false;
        type = match.Groups[1].Value == "DLTINS" ? FirdsFileType.Delta : FirdsFileType.Full;
        return true;
    }

    // The CFI category a full file covers ('E' or 'C'); a delta covers every category and answers null.
    public static char? FullCategory(string fileName) =>
        fileName != null
        && fileName.StartsWith("FULINS_", StringComparison.Ordinal)
        && fileName.Length > 7
            ? fileName[7]
            : null;
}
