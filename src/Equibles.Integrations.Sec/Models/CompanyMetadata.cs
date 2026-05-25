using System.Globalization;

namespace Equibles.Integrations.Sec.Models;

public class CompanyMetadata
{
    public string Cik { get; set; }
    public string EntityType { get; set; }
    public List<string> Exchanges { get; set; } = [];

    /// <summary>
    /// SEC's raw current fiscal year-end as a 4-character "MMDD" string
    /// (e.g. "0928" for Apple). Null/blank when the filer never reported one.
    /// </summary>
    public string FiscalYearEnd { get; set; }

    public string Website { get; set; }

    /// <summary>
    /// Month (1-12) parsed from <see cref="FiscalYearEnd"/>, or null when the
    /// value is missing or not a valid calendar MMDD date.
    /// </summary>
    public int? FiscalYearEndMonth => ParseFiscalYearEnd().Month;

    /// <summary>
    /// Day of month parsed from <see cref="FiscalYearEnd"/>, or null when the
    /// value is missing or not a valid calendar MMDD date.
    /// </summary>
    public int? FiscalYearEndDay => ParseFiscalYearEnd().Day;

    // Memoised against the trimmed source so the common
    // "read Month then read Day" access pattern parses once, and a re-set of
    // FiscalYearEnd re-parses (this is a deserialisation DTO).
    private string _parsedSource;
    private (int? Month, int? Day) _parsed;

    /// <summary>
    /// Parses the SEC "MMDD" fiscal year-end string. Returns (null, null) for
    /// anything that is not exactly four digits forming a real calendar date
    /// (1-12 month and a day that exists in that month, Feb 29 allowed) — SEC
    /// occasionally emits blanks or malformed values, and a silently wrong
    /// month would corrupt every downstream fiscal-quarter calculation while a
    /// bogus day (e.g. "0931") would persist a date that does not exist.
    /// </summary>
    private (int? Month, int? Day) ParseFiscalYearEnd()
    {
        var value = FiscalYearEnd?.Trim();
        if (_parsedSource == value)
        {
            return _parsed;
        }

        _parsedSource = value;
        _parsed = Compute(value);
        return _parsed;

        static (int? Month, int? Day) Compute(string value)
        {
            if (
                value is not { Length: 4 }
                || !int.TryParse(
                    value.AsSpan(0, 2),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var month
                )
                || !int.TryParse(
                    value.AsSpan(2, 2),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var day
                )
                || month is < 1 or > 12
                // Leap year (2000) so a legitimate Feb-29 fiscal year-end is
                // accepted; Feb-30, Sep-31, Apr-31, etc. are rejected.
                || day < 1
                || day > DateTime.DaysInMonth(2000, month)
            )
            {
                return (null, null);
            }

            return (month, day);
        }
    }

    public bool IsOperatingCompany =>
        string.Equals(EntityType, "operating", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when SEC's submissions document lists at least one real stock exchange.
    /// Subsidiaries that file with the SEC but aren't separately listed have an empty
    /// or OTC-only exchanges list, which is the strongest signal that they don't own
    /// the public ticker they share with their parent.
    /// </summary>
    public bool IsListed =>
        Exchanges.Any(e =>
            !string.IsNullOrWhiteSpace(e)
            && !string.Equals(e, "OTC", StringComparison.OrdinalIgnoreCase)
        );
}
