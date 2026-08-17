using System.Globalization;
using System.Text.RegularExpressions;

namespace Equibles.Congress.Data;

/// <summary>
/// Renders the seat stored on <see cref="Models.CongressMember.StateDistrict"/>.
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

        var value = stateDistrict.Trim();
        var match = SeatRegex().Match(value);
        if (!match.Success)
            return value;

        var state = match.Groups["state"].Value.ToUpperInvariant();
        var district = int.Parse(match.Groups["district"].Value, CultureInfo.InvariantCulture);
        return district == 0 ? $"{state} At-Large" : $"{state}-{district}";
    }

    [GeneratedRegex(@"^(?<state>[A-Za-z]{2})(?<district>\d{1,2})$")]
    private static partial Regex SeatRegex();
}
