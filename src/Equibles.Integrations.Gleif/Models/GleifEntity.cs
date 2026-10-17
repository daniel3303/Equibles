namespace Equibles.Integrations.Gleif.Models;

// One GLEIF legal entity record: its names, where it is registered and what kind of entity it is.
public class GleifEntity
{
    public string LegalEntityIdentifier { get; set; }
    public string LegalName { get; set; }
    public string LegalNameLanguage { get; set; }

    // Other and transliterated names, such as the English name of an entity registered in Korean.
    public List<GleifEntityName> OtherNames { get; set; } = [];

    // ISO 3166-1 alpha-2 country of the legal address.
    public string Country { get; set; }
    public string Jurisdiction { get; set; }

    // GLEIF's entity category, such as GENERAL, BRANCH, FUND or RESIDENT_GOVERNMENT_ENTITY; null when unstated.
    public string Category { get; set; }
    public string EntityStatus { get; set; }
    public string RegistrationStatus { get; set; }
    public Uri SourceUrl { get; set; }
}
