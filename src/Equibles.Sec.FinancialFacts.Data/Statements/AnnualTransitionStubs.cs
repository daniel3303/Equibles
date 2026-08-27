using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;

namespace Equibles.Sec.FinancialFacts.Data.Statements;

/// <summary>
/// Corroboration for sub-annual durations stamped <see cref="SecFiscalPeriod.FullYear"/>.
/// The annual bucket is the enum's zero value, so it collects unclassified periods on
/// top of the discrete-Q4-under-fp=FY case — a FullYear bucket with no ordinary
/// annual-span fact holds either a genuine fiscal-calendar transition stub (a short
/// "year" filed when the issuer moved its year end) or a misclassified sub-annual span
/// (a six/nine-month year-to-date, a lone discrete quarter). The two are told apart by
/// the surrounding fiscal years: a real transition stub is seamed into the company's
/// annual timeline on BOTH sides — the previous annual window ends where the stub
/// starts and the next annual window starts where the stub ends. A year-to-date span
/// only abuts on the start side (its own fiscal year began there) and a discrete
/// fourth quarter only on the end side (the next year begins there), so requiring both
/// seams refuses each while keeping the stub. A stub whose next annual window has not
/// been filed yet fails closed: absent beats publishing a span that may be a fraction
/// of a year as the year.
/// </summary>
public static class AnnualTransitionStubs
{
    // Aligned with the annual gates used by every per-period picker (350-380 days,
    // sized for 52/53-week calendars). A span at or above the floor is an ordinary
    // annual fact and never needs corroboration; one above the ceiling is multi-year
    // or inception-to-date and is never publishable as a year — without the floor cut
    // a two-year cumulative would corroborate, because the years on either side of it
    // abut it exactly the way a transition stub's neighbours do.
    public const int MinAnnualSpanDays = 350;
    public const int MaxAnnualSpanDays = 380;

    // Calendar drift headroom for the seam test: a 52/53-week filer's year end moves
    // by up to a week against the calendar, so "ends where the stub starts" is exact
    // to within that drift (the same allowance the year-turn stamping rules use).
    public const int AbutmentToleranceDays = 7;

    /// <summary>
    /// True when <paramref name="stub"/> is a sub-annual duration seamed into the
    /// company's annual timeline on both sides by annual-span durations found in
    /// <paramref name="context"/>. A null or empty context corroborates nothing.
    /// </summary>
    public static bool IsCorroborated(FinancialFact stub, IEnumerable<FinancialFact> context)
    {
        if (context == null)
            return false;
        return IsCorroborated(
            stub.PeriodStart,
            stub.PeriodEnd,
            context
                .Where(f => f.PeriodType == FactPeriodType.Duration)
                .Select(f => (f.PeriodStart, f.PeriodEnd))
        );
    }

    /// <summary>
    /// The date-level rule, for callers whose facts are projected records rather than
    /// entities. <paramref name="durations"/> may contain any spans; only annual-span
    /// windows (350-380 days) corroborate.
    /// </summary>
    public static bool IsCorroborated(
        DateOnly stubStart,
        DateOnly stubEnd,
        IEnumerable<(DateOnly Start, DateOnly End)> durations
    )
    {
        var stubSpan = stubEnd.DayNumber - stubStart.DayNumber;
        if (stubSpan < 0 || stubSpan >= MinAnnualSpanDays)
            return false;

        var previousYearAbuts = false;
        var nextYearAbuts = false;
        foreach (var (start, end) in durations)
        {
            var span = end.DayNumber - start.DayNumber;
            if (span is < MinAnnualSpanDays or > MaxAnnualSpanDays)
                continue;
            if (Math.Abs(end.DayNumber - (stubStart.DayNumber - 1)) <= AbutmentToleranceDays)
                previousYearAbuts = true;
            if (Math.Abs(start.DayNumber - (stubEnd.DayNumber + 1)) <= AbutmentToleranceDays)
                nextYearAbuts = true;
            if (previousYearAbuts && nextYearAbuts)
                return true;
        }

        return false;
    }
}
