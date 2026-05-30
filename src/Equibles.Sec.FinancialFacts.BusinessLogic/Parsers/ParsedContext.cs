using Equibles.Sec.FinancialFacts.BusinessLogic.Models;

namespace Equibles.Sec.FinancialFacts.BusinessLogic.Parsers;

// Parsed xbrli:context shared by StandaloneXbrlParser (LINQ-to-XML) and
// InlineXbrlParser (AngleSharp): both build the same context shape from their
// respective engines, so the type stays identical across the two.
internal record struct ParsedContext(
    bool IsInstant,
    DateOnly Start,
    DateOnly End,
    List<ParsedXbrlDimension> Dimensions
);
