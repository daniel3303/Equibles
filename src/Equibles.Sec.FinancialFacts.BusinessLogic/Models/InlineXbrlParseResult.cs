namespace Equibles.Sec.FinancialFacts.BusinessLogic.Models;

/// <summary>
/// Everything one pass over an inline-XBRL envelope yields: the numeric facts
/// and the cover-page 12(b) security listings. Combined so the multi-megabyte
/// document is DOM-parsed once for both.
/// </summary>
public class InlineXbrlParseResult
{
    public List<ParsedXbrlFact> Facts { get; set; } = [];
    public List<ParsedSecurityListing> CoverListings { get; set; } = [];
}
