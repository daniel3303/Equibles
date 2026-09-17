using Equibles.Sec.FinancialFacts.BusinessLogic.Models;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

// Parsed xbrli:context shared by StandaloneXbrlParser (LINQ-to-XML) and
// InlineXbrlParser (AngleSharp): both build the same context shape from their
// respective engines, so the type stays identical across the two.
internal record struct ParsedContext(
    bool IsInstant,
    DateOnly Start,
    DateOnly End,
    List<ParsedXbrlDimension> Dimensions,
    string ConsolidatedCik = null,
    // The same unqualified-context identity under the ISO 17442 scheme a European report states instead of a
    // CIK. It sits beside the CIK rather than generalising it, so the SEC path is byte-identical.
    string ConsolidatedLei = null
);
