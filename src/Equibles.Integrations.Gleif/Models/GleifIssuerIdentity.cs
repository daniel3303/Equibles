namespace Equibles.Integrations.Gleif.Models;

public class GleifIssuerIdentity
{
    public string RequestedIsin { get; set; }
    public string RequestedLei { get; set; }
    public string LegalEntityIdentifier { get; set; }
    public string LegalName { get; set; }
    public string Jurisdiction { get; set; }
    public string EntityStatus { get; set; }
    public string RegistrationStatus { get; set; }
    public Uri SourceUrl { get; set; }
    public List<string> RelatedIsins { get; set; } = [];

    // GLEIF's reported ISIN total. RelatedIsins is the whole set only when this is within the client's
    // enumeration bound; above it the list is just the requested ISIN, so sibling-ISIN merge checks do not run.
    public int RelatedIsinCount { get; set; }
    public List<string> ResponseBodies { get; set; } = [];
}
