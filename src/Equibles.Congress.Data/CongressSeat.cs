using System.Globalization;
using System.Text.RegularExpressions;

namespace Equibles.Congress.Data;

/// <summary>
/// Reads and renders the seat stored on <see cref="Models.CongressMember.StateDistrict"/>.
/// The House Clerk publishes it as a postal code with a zero-padded district
/// ("SC05"), which is a key, not a label — district 00 means the state elects a
/// single at-large representative rather than a district numbered zero. A value
/// that does not match that shape is shown exactly as stored, so an unfamiliar
/// format is visible rather than silently reformatted into something wrong.
/// </summary>
public static partial class CongressSeat
{
    public static string Format(string stateDistrict)
    {
        if (string.IsNullOrWhiteSpace(stateDistrict))
            return null;

        if (!TryParse(stateDistrict, out var state, out var district))
            return stateDistrict.Trim();

        return district == 0 ? $"{state} At-Large" : $"{state}-{district}";
    }

    /// <summary>
    /// Splits a seat key into its upper-case postal code and district number. A one- or two-digit
    /// district parses as written, and "AL" (at-large) reads as district 0.
    /// </summary>
    public static bool TryParse(string stateDistrict, out string state, out short district)
    {
        state = null;
        district = 0;
        if (string.IsNullOrWhiteSpace(stateDistrict))
            return false;

        var match = SeatRegex().Match(stateDistrict.Trim());
        if (!match.Success)
            return false;

        state = match.Groups["state"].Value.ToUpperInvariant();
        var districtText = match.Groups["district"].Value;
        district = string.Equals(districtText, "AL", StringComparison.OrdinalIgnoreCase)
            ? (short)0
            : short.Parse(districtText, CultureInfo.InvariantCulture);
        return true;
    }

    [GeneratedRegex(@"^(?<state>[A-Za-z]{2})(?<district>\d{1,2}|[Aa][Ll])$")]
    private static partial Regex SeatRegex();
}
