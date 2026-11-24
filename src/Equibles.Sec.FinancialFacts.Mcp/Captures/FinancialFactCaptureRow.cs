using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.Sec.FinancialFacts.Mcp.Captures;

// One printed fact: the value as the table prints it (today's split basis, or as filed where AsFiled), its unit and the
// period it covers. FiscalYear is the company's own year label, as printed; Summed marks a figure summed from parts,
// Derived a quarter worked out from the year's cumulative figures.
public sealed record FinancialFactCaptureRow(
    int FiscalYear,
    SecFiscalPeriod Period,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal Value,
    string Unit,
    bool AsFiled,
    bool Summed,
    string Form,
    DateOnly Filed,
    bool Derived = false
);
