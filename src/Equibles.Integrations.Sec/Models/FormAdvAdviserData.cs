namespace Equibles.Integrations.Sec.Models;

/// <summary>
/// One investment adviser parsed from a row of the SEC's bulk Form ADV download. Carries the
/// raw Form ADV Part 1A values the importer persists; identity is the Organization CRD number.
/// Monetary and count fields are null when the firm left the corresponding item blank.
/// </summary>
public class FormAdvAdviserData
{
    /// <summary>Organization CRD number — the adviser's stable primary identifier.</summary>
    public int Crd { get; set; }

    /// <summary>SEC file number, e.g. "801-54739"; null for firms without one.</summary>
    public string SecNumber { get; set; }

    public string LegalName { get; set; }
    public string PrimaryBusinessName { get; set; }
    public string MainOfficeCity { get; set; }
    public string MainOfficeState { get; set; }
    public string MainOfficeCountry { get; set; }
    public string WebsiteAddress { get; set; }
    public string SecStatus { get; set; }

    public int? NumberOfEmployees { get; set; }
    public long? TotalRegulatoryAum { get; set; }
    public long? DiscretionaryAum { get; set; }
    public long? NonDiscretionaryAum { get; set; }

    public bool ChargesPercentageOfAum { get; set; }
    public bool ChargesHourly { get; set; }
    public bool ChargesSubscription { get; set; }
    public bool ChargesFixed { get; set; }
    public bool ChargesCommissions { get; set; }
    public bool ChargesPerformanceBased { get; set; }
    public bool ChargesOther { get; set; }
}
