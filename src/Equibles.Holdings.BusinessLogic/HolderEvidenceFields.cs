namespace Equibles.Holdings.BusinessLogic;

/// <summary>The filed fields holder classification evidence records, one name per field.</summary>
public static class HolderEvidenceFields
{
    /// <summary>The Form ADV roster an adviser is listed in; the value is a <c>FormAdvRegistration</c> name.</summary>
    public const string AdvRegistration = "Registration";

    public const string Sic = "Sic";

    /// <summary>A reporting person's 13D/13G cover-page type codes, as <c>ReportingPersonTypes</c> names.</summary>
    public const string TypeOfReportingPerson = "TypeOfReportingPerson";

    /// <summary>A 13G's Item 3 category of the persons filing, as <c>ReportingPersonTypes</c> names.</summary>
    public const string TypeOfPersonFiling = "TypeOfPersonFiling";

    /// <summary>An N-CEN registrant's investment company type, e.g. <c>N-1A</c>.</summary>
    public const string InvestmentCompanyType = "InvestmentCompanyType";

    public const string AdvRegistered = "Registered";
    public const string AdvExemptReporting = "ExemptReporting";
}
