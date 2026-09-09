using System.Text;
using Equibles.CommonStocks.Data.Models;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Media.BusinessLogic;
using Equibles.Sec.Data.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic;
using Equibles.Sec.FinancialFacts.BusinessLogic.Models;
using Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;
using Equibles.Sec.FinancialFacts.Data.Enums;
using Equibles.Sec.FinancialFacts.Data.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Equibles.Sec.FinancialFacts.HostedService.Services;

/// <summary>Reads historical calendars from reported annual spans and captured filing metadata.</summary>
[Service]
public class FiscalCalendarEvidenceReader(
    IServiceScopeFactory scopeFactory,
    IFileManager fileManager,
    InlineXbrlParser inlineParser
)
{
    private const int CalendarDocumentBatchSize = 16;
    private const long MaxCalendarEnvelopeBytes = 50 * 1024 * 1024;

    public async Task<HistoricalFiscalCalendar> Read(
        CommonStock stock,
        IReadOnlyCollection<(DateOnly Start, DateOnly End)> incomingAnnualPeriods,
        CancellationToken cancellationToken
    )
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var annualDates = await db.Set<Document>()
            .Where(d =>
                d.CommonStockId == stock.Id
                && (
                    d.DocumentType == DocumentType.TenK
                    || d.DocumentType == DocumentType.TwentyF
                    || d.DocumentType == DocumentType.FortyF
                )
            )
            .Select(d => d.ReportingForDate)
            .Distinct()
            .ToListAsync(cancellationToken);
        var stored = await db.Set<FinancialFact>()
            .Where(f =>
                f.CommonStockId == stock.Id
                && f.DimensionsKey == ""
                && f.PeriodType == FactPeriodType.Duration
                && (
                    f.Form == DocumentType.TenK
                    || f.Form == DocumentType.TwentyF
                    || f.Form == DocumentType.FortyF
                )
                && f.PeriodEnd >= f.PeriodStart.AddDays(350)
                && f.PeriodEnd <= f.PeriodStart.AddDays(380)
                && annualDates.Contains(f.PeriodEnd)
            )
            .Select(f => new { f.PeriodStart, f.PeriodEnd })
            .Distinct()
            .ToListAsync(cancellationToken);
        var annualPeriods = stored
            .Select(f => (Start: f.PeriodStart, End: f.PeriodEnd))
            .Concat(
                incomingAnnualPeriods.Where(p =>
                    annualDates.Contains(p.End)
                    && p.End.DayNumber - p.Start.DayNumber is >= 350 and <= 380
                )
            )
            .Distinct()
            .ToArray();
        var observations = new List<ParsedFiscalYearEnd>();
        var calendarChanged =
            stock.FiscalYearEndMonth is >= 1 and <= 12
            && stock.FiscalYearEndDay is >= 1 and <= 31
            && annualPeriods.Any(p => !MatchesCurrentCalendar(p.End, stock));
        if (calendarChanged)
        {
            // Retain metadata for gaps between ordinary annual spans, including transition
            // years, even after a later annual report adopts the current calendar.
            var candidates = await db.Set<Document>()
                .Where(d =>
                    d.CommonStockId == stock.Id
                    && (
                        d.DocumentType == DocumentType.TenQ
                        || d.DocumentType == DocumentType.TenK
                        || d.DocumentType == DocumentType.TwentyF
                        || d.DocumentType == DocumentType.FortyF
                    )
                )
                .Select(d => new { d.Id, d.ReportingForDate })
                .ToListAsync(cancellationToken);
            var evidenceIds = candidates
                .Where(d =>
                    !annualPeriods.Any(p =>
                        d.ReportingForDate >= p.Start && d.ReportingForDate <= p.End
                    )
                )
                .OrderBy(d => d.ReportingForDate)
                .ThenBy(d => d.Id)
                .Select(d => d.Id)
                .ToArray();
            var ciks = stock.SecondaryCiks.Append(stock.Cik).ToArray();
            // A missing historical envelope only withholds evidence for its own periods.
            // Read every gap in bounded batches so long histories cannot starve newer filings.
            foreach (var batch in evidenceIds.Chunk(CalendarDocumentBatchSize))
            {
                var documents = await db.Set<Document>()
                    .AsNoTracking()
                    .Where(d =>
                        batch.Contains(d.Id)
                        && d.XbrlStatus == XbrlCaptureStatus.Captured
                        && d.XbrlType == XbrlType.InlineIxbrl
                        && d.XbrlContent != null
                        && d.XbrlUncompressedSize != null
                        && d.XbrlUncompressedSize <= MaxCalendarEnvelopeBytes
                    )
                    .Include(d => d.XbrlContent)
                    .ToListAsync(cancellationToken);
                foreach (var document in documents)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var bytes = GzipCompressor.Decompress(
                        await fileManager.GetContent(document.XbrlContent)
                    );
                    if (bytes.LongLength > MaxCalendarEnvelopeBytes)
                        continue;
                    var parsed = inlineParser.ParseEnvelope(Encoding.UTF8.GetString(bytes));
                    observations.AddRange(
                        parsed.FiscalYearEnds.Where(o =>
                            o.PeriodEnd == document.ReportingForDate
                            && ciks.Any(cik => SameCik(o.Cik, cik))
                        )
                    );
                }
            }
        }
        // HistoricalFiscalCalendar refuses missing or conflicting evidence per fact;
        // neither condition invalidates independent, source-backed annual spans.
        return new HistoricalFiscalCalendar(
            annualPeriods,
            observations,
            stock.FiscalYearEndMonth,
            stock.FiscalYearEndDay,
            calendarChanged
        );
    }

    private static bool SameCik(string left, string right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && left.Trim().TrimStart('0') == right.Trim().TrimStart('0');

    private static bool MatchesCurrentCalendar(DateOnly annualEnd, CommonStock stock)
    {
        if (
            stock.FiscalYearEndMonth is not (>= 1 and <= 12)
            || stock.FiscalYearEndDay is not (>= 1 and <= 31)
        )
            return false;
        var month = stock.FiscalYearEndMonth.Value;
        var day = Math.Min(
            stock.FiscalYearEndDay.Value,
            DateTime.DaysInMonth(annualEnd.Year, month)
        );
        return new[] { annualEnd.Year - 1, annualEnd.Year, annualEnd.Year + 1 }
            .Where(year => year is >= 1 and <= 9999)
            .Any(year =>
                Math.Abs(
                    new DateOnly(
                        year,
                        month,
                        Math.Min(day, DateTime.DaysInMonth(year, month))
                    ).DayNumber - annualEnd.DayNumber
                ) <= 14
            );
    }
}
