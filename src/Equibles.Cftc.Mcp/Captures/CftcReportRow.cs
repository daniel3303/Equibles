namespace Equibles.Cftc.Mcp.Captures;

// One weekly COT report of a GetCftcPositioning answer: the contract counts behind its cells, and each cell as printed.
public sealed record CftcReportRow(
    DateOnly ReportDate,
    long OpenInterest,
    string OpenInterestText,
    long CommercialLong,
    string CommercialLongText,
    long CommercialShort,
    string CommercialShortText,
    long NonCommercialLong,
    string NonCommercialLongText,
    long NonCommercialShort,
    string NonCommercialShortText,
    long NonCommercialSpreads,
    string NonCommercialSpreadsText
);
