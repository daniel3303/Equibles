namespace Equibles.CommonStocks.Data.Models;

/// <summary>What a blank-exchange registrant's EDGAR filings proved about an exchange listing.</summary>
public enum SecListingEvidenceOutcome
{
    /// <summary>No exchange certification, or none preceded by the registrant's 8-A12B.</summary>
    NoCertification = 0,

    /// <summary>Certified, but no final prospectus (424B4) filed since the certification.</summary>
    NoFinalProspectus = 1,

    /// <summary>A delisting, deregistration or withdrawal was filed after the certification.</summary>
    Withdrawn = 2,

    /// <summary>The final prospectus is older than the admission window.</summary>
    Stale = 3,

    /// <summary>8-A12B, exchange certification and a final prospectus, in that order and recent.</summary>
    Admitted = 4,
}
