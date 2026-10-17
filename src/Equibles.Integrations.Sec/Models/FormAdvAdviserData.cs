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

    /// <summary>The roster's <c>Firm Type</c> ("Registered" or "ERA"); null in layouts without it.</summary>
    public string FirmType { get; set; }

    /// <summary>The first CIK the adviser reports, as filed.</summary>
    public string Cik { get; set; }

    public int? CikCount { get; set; }

    /// <summary>Item 1.P as filed; placeholders such as "N/A" are not filtered here.</summary>
    public string Lei { get; set; }

    public DateOnly? LatestFilingDate { get; set; }

    /// <summary>Item 3.A as filed, e.g. "Limited Liability Company".</summary>
    public string OrganizationForm { get; set; }

    /// <summary>Checked Item 2.A boxes; item (n) is bit n-1.</summary>
    public int RegistrationBasisItems { get; set; }

    /// <summary>Checked Item 2.B boxes; item (n) is bit n-1.</summary>
    public int ExemptionBasisItems { get; set; }

    /// <summary>Checked Item 6.A boxes; item (n) is bit n-1.</summary>
    public int OtherBusinessItems { get; set; }

    /// <summary>Checked Item 7.A boxes; item (n) is bit n-1.</summary>
    public int RelatedPersonItems { get; set; }

    public bool? AdvisesPrivateFunds { get; set; }
    public int? PrivateFundCount { get; set; }
    public int? HedgeFundCount { get; set; }
    public int? PrivateEquityFundCount { get; set; }
    public int? VentureCapitalFundCount { get; set; }
    public int? RealEstateFundCount { get; set; }
    public int? LiquidityFundCount { get; set; }
    public int? SecuritizedAssetFundCount { get; set; }
    public int? OtherPrivateFundCount { get; set; }
    public long? PrivateFundGrossAssets { get; set; }

    /// <summary>Item 5.D client types the adviser reports clients or assets for.</summary>
    public List<FormAdvClientTypeData> ClientTypes { get; set; } = [];
}
