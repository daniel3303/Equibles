using Equibles.CommonStocks.Data.Models;
using Equibles.Holdings.BusinessLogic.Models;

namespace Equibles.Holdings.Mcp.Captures;

/// <summary>
/// The rows a GetTopHolders answer lists, exactly as it prints them: ranked, paged and filtered,
/// shares restated onto today's split basis, with the totals over every institution. A combined
/// view sets <see cref="CarriedFrom"/>; <see cref="ReportDates"/> is the quarter list the answer
/// resolved its date from, newest first.
/// </summary>
public sealed record TopHoldersCapture(
    EquityIssuer Stock,
    string ListedTicker,
    bool ExactListingScope,
    DateOnly ReportDate,
    DateOnly? CarriedFrom,
    IReadOnlyList<DateOnly> ReportDates,
    int TotalInstitutions,
    int TotalRows,
    int Offset,
    long TotalShares,
    long TotalValue,
    long CallShares,
    long PutShares,
    string FilterDescription,
    DateOnly? StyleAsOf,
    IReadOnlyList<TopHoldersCaptureRow> Rows,
    IReadOnlyList<HoldingsExcludedFiler> Withheld
);
