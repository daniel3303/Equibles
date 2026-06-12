namespace Equibles.Integrations.Wikidata.Contracts;

public interface IWikidataClient
{
    /// <summary>
    /// Returns the official website (P856) of each company whose SEC CIK (P5531)
    /// appears in <paramref name="ciks"/>, keyed by the CIK as passed in. CIKs are
    /// matched zero-padded to 10 digits — Wikidata's storage format — so callers
    /// may pass either padded or bare values. CIKs Wikidata doesn't know (or knows
    /// without a website) are absent from the result.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetOfficialWebsitesByCik(
        IReadOnlyCollection<string> ciks,
        CancellationToken cancellationToken
    );
}
