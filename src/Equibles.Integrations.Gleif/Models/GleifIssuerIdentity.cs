namespace Equibles.Integrations.Gleif.Models;

public class GleifIssuerIdentity
{
    public string RequestedIsin { get; set; }
    public string LegalEntityIdentifier { get; set; }
    public string LegalName { get; set; }
    public string Jurisdiction { get; set; }
    public string EntityStatus { get; set; }
    public string RegistrationStatus { get; set; }
    public Uri SourceUrl { get; set; }
    public List<string> RelatedIsins { get; set; } = [];
    public List<string> ResponseBodies { get; set; } = [];
}
