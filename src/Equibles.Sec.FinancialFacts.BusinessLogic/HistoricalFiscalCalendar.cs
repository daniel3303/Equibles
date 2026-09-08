using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Equibles.Sec.FinancialFacts.BusinessLogic.Models;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.FiscalPeriods;

namespace Equibles.Sec.FinancialFacts.BusinessLogic;

/// <summary>Uses a reported annual span or exact-period filing metadata before current metadata.</summary>
public sealed class HistoricalFiscalCalendar(
    IReadOnlyCollection<(DateOnly Start, DateOnly End)> annualPeriods,
    IReadOnlyCollection<ParsedFiscalYearEnd> reportedYearEnds,
    int? currentMonth,
    int? currentDay,
    bool requiresHistoricalEvidence = false
)
{
    public bool RequiresHistoricalEvidence { get; } = requiresHistoricalEvidence;

    public bool HasEvidence(DateOnly start, DateOnly end) =>
        annualPeriods.Any(period => start >= period.Start && end <= period.End)
        || reportedYearEnds.Any(observation => Applies(observation, start, end));

    public bool RefusesCalendar(DateOnly start, DateOnly end)
    {
        var annualEnds = annualPeriods
            .Where(p => start >= p.Start && end <= p.End)
            .Select(p => p.End)
            .Distinct()
            .ToArray();
        if (annualEnds.Length > 0)
            return annualEnds.Length > 1;
        var calendars = reportedYearEnds
            .Where(p => Applies(p, start, end))
            .Select(p => (p.Month, p.Day))
            .Distinct()
            .Count();
        return calendars > 1 || (RequiresHistoricalEvidence && calendars == 0);
    }

    public string Fingerprint { get; } =
        Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    string.Join(
                        "|",
                        new[]
                        {
                            FormattableString.Invariant(
                                $"{currentMonth}:{currentDay}:{requiresHistoricalEvidence}"
                            ),
                        }
                            .Concat(
                                annualPeriods
                                    .Distinct()
                                    .OrderBy(p => p.Start)
                                    .ThenBy(p => p.End)
                                    .Select(p =>
                                        $"A:{p.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}:{p.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
                                    )
                            )
                            .Concat(
                                reportedYearEnds
                                    .Select(p => (p.PeriodStart, p.PeriodEnd, p.Month, p.Day))
                                    .Distinct()
                                    .OrderBy(p => p.PeriodEnd)
                                    .ThenBy(p => p.PeriodStart)
                                    .ThenBy(p => p.Month)
                                    .ThenBy(p => p.Day)
                                    .Select(p =>
                                        FormattableString.Invariant(
                                            $"F:{p.PeriodStart:yyyy-MM-dd}:{p.PeriodEnd:yyyy-MM-dd}:{p.Month}:{p.Day}"
                                        )
                                    )
                            )
                    )
                )
            )
        );

    public (int Year, SecFiscalPeriod Period)? Resolve(DateOnly start, DateOnly end)
    {
        var annualEnds = annualPeriods
            .Where(period => start >= period.Start && end <= period.End)
            .Select(period => period.End)
            .Distinct()
            .ToArray();
        if (annualEnds.Length > 1)
            return null;
        if (annualEnds.Length == 1)
            return ReportedFiscalPeriodResolver.Resolve(
                start,
                end,
                annualEnds[0].Month,
                annualEnds[0].Day,
                classifyInterimInstants: true
            );

        var sourceCalendars = reportedYearEnds
            .Where(observation => Applies(observation, start, end))
            .Select(observation => (observation.Month, observation.Day))
            .Distinct()
            .ToArray();
        if (sourceCalendars.Length > 1)
            return null;
        if (sourceCalendars.Length == 0 && RequiresHistoricalEvidence)
            return null;
        var (month, day) =
            sourceCalendars.Length == 1
                ? ((int?)sourceCalendars[0].Month, (int?)sourceCalendars[0].Day)
                : (currentMonth, currentDay);
        return ReportedFiscalPeriodResolver.Resolve(
            start,
            end,
            month,
            day,
            classifyInterimInstants: true
        );
    }

    private static bool Applies(ParsedFiscalYearEnd observation, DateOnly start, DateOnly end) =>
        observation.PeriodEnd == end
        || (start >= observation.PeriodStart && end <= observation.PeriodEnd);
}
