namespace Equibles.CommonStocks.Repositories.Models;

// A listed ISIN due a GLEIF answer, with the LEI its issuer already holds (null when it holds none).
public sealed record IsinLegalEntityLookupDue(string Isin, string LegalEntityIdentifier);
