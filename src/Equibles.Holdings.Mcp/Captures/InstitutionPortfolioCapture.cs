using Equibles.Holdings.Data.Models;

namespace Equibles.Holdings.Mcp.Captures;

/// <summary>
/// The rows a GetInstitutionPortfolio answer lists, exactly as it prints them: value-ranked and paged,
/// shares restated onto today's split basis, with the quarter's totals over every row.
/// <see cref="ReportDates"/> is the holder's 13F quarter list the answer resolved its date from,
/// newest first.
/// </summary>
public sealed record InstitutionPortfolioCapture(
    InstitutionalHolder Holder,
    DateOnly ReportDate,
    IReadOnlyList<DateOnly> ReportDates,
    int Offset,
    int TotalRows,
    int TotalPositions,
    long TotalValue,
    int UnvaluedPositions,
    int? DeclaredPositionCount,
    long? DeclaredTotalValue,
    IReadOnlyList<InstitutionPortfolioCaptureRow> Rows
);
