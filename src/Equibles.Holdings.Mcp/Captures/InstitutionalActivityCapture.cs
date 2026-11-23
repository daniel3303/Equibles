using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.BusinessLogic.Models;

namespace Equibles.Holdings.Mcp.Captures;

/// <summary>
/// The buyers and sellers a GetTopInstitutionalBuyersSellers answer ranks, exactly as it prints them, shares
/// restated onto today's split basis. The four counts cover every filer the comparison ranks from, not only the
/// rows listed; <see cref="Comparison"/> says which filers were left out and why, and
/// <see cref="ReportDates"/> is the quarter list the answer resolved its date from, newest first.
/// </summary>
public sealed record InstitutionalActivityCapture(
    EquityIssuer Stock,
    string ListedTicker,
    bool ExactListingScope,
    DateOnly ReportDate,
    DateOnly PreviousReportDate,
    IReadOnlyList<DateOnly> ReportDates,
    int MaxResults,
    bool FilingWindowOpen,
    int Adding,
    int NewPositions,
    int Cutting,
    int SoldOut,
    IReadOnlyList<InstitutionalActivityCaptureRow> Buyers,
    IReadOnlyList<InstitutionalActivityCaptureRow> Sellers,
    HoldingsComparisonStatus Comparison
);
