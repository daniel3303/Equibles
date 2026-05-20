namespace Equibles.Sec.FinancialFacts.BusinessLogic.Models;

/// <summary>
/// One explicit XBRL dimension on a parsed fact — the (axis, member) pair
/// extracted from an <c>xbrldi:explicitMember</c> element inside an
/// <c>xbrli:context</c>'s segment/scenario. Both sides are full QNames
/// (<c>prefix:localName</c>) preserved exactly as the source document
/// declared them, since member prefixes routinely point at filer-specific
/// extension namespaces (<c>aapl:</c>, <c>msft:</c>, …) the consumer is
/// expected to interpret.
/// </summary>
public class ParsedXbrlDimension
{
    public string Axis { get; init; }

    public string Member { get; init; }
}
