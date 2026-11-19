using System.ComponentModel.DataAnnotations;
using Equibles.CommonStocks.Data.Helpers;

namespace Equibles.CommonStocks.Data.Models;

/// <summary>
/// The latest read of a registrant's EDGAR filings for proof of an exchange listing, kept for a
/// registrant whose every row in SEC's ticker directory has a blank exchange. It is the audit
/// stamp of an admission and paces how often EDGAR is read again for that registrant.
/// </summary>
public class SecListingEvidenceCheck
{
    /// <summary>Unpadded, the form <see cref="CikNormalizer.Canonicalize"/> gives.</summary>
    [Key]
    [MaxLength(CikNormalizer.MaxLength)]
    public string Cik { get; set; }

    /// <summary>The tickers SEC's directory listed for the registrant at the check.</summary>
    public List<string> Tickers { get; set; } = [];

    public DateTime CheckedAt { get; set; }

    public SecListingEvidenceOutcome Outcome { get; set; }

    [MaxLength(32)]
    public string RegistrationAccessionNumber { get; set; }

    [MaxLength(32)]
    public string CertificationAccessionNumber { get; set; }

    [MaxLength(32)]
    public string ProspectusAccessionNumber { get; set; }

    public DateOnly? ProspectusFiledOn { get; set; }
}
