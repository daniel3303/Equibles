using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// The kind of raw XBRL envelope captured for a filing. A single filing can yield
/// both: the inline iXBRL embedded in the primary HTML document, and a separate
/// standalone XBRL <c>.xml</c> instance listed among the filing's artifacts.
/// </summary>
public enum RawFilingArtifactType
{
    /// <summary>
    /// The raw primary document containing inline XBRL (<c>ix:header</c>,
    /// <c>ix:nonFraction</c>, <c>ix:nonNumeric</c>), captured before the HTML
    /// normalizer strips it.
    /// </summary>
    [Display(Name = "Inline iXBRL")]
    InlineIxbrl = 0,

    /// <summary>
    /// A standalone XBRL instance document fetched from the filing's artifact list.
    /// </summary>
    [Display(Name = "Standalone XBRL")]
    StandaloneXbrl = 1,
}
