using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// The kind of raw XBRL envelope captured for a <see cref="Document"/>. A filing
/// carries one or the other depending on its era: modern filings embed inline iXBRL
/// in their HTML documents, while older filings ship a separate standalone instance.
/// </summary>
public enum XbrlType
{
    /// <summary>
    /// The filing's inline document set carries inline XBRL (<c>ix:header</c>,
    /// <c>ix:nonFraction</c>, <c>ix:nonNumeric</c>): every HTML document with inline markers,
    /// joined primary first, captured before the HTML normalizer strips it.
    /// </summary>
    [Display(Name = "Inline iXBRL")]
    InlineIxbrl = 0,

    /// <summary>
    /// A standalone XBRL instance document carried as a separate envelope in the filing.
    /// </summary>
    [Display(Name = "Standalone XBRL")]
    StandaloneXbrl = 1,

    [Display(Name = "xBRL-JSON")]
    JsonXbrl = 2,
}
