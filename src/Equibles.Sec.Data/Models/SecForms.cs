namespace Equibles.Sec.Data.Models;

/// <summary>The kind of SEC form, for surfaces that group or describe forms.</summary>
public enum SecFormFamily
{
    Periodic,
    Current,
    Proxy,
    Registration,
    TenderOffer,
    LateFilingNotice,
    AnnualReportToHolders,
    Ownership,
    Offering,
    Fund,
}

/// <summary>An SEC form the platform recognizes. Its EDGAR form string is the type's display name.</summary>
public sealed record SecForm(
    DocumentType Type,
    SecFormFamily Family,
    DocumentType AmendmentOf = null,
    bool SyncedByDefault = true
)
{
    public string EdgarForm => Type.DisplayName;
}

/// <summary>
/// The one catalog of SEC forms: which document types are EDGAR forms, their family and amendment
/// lineage, and which ones the filing scraper syncs unless configured otherwise.
/// </summary>
public static class SecForms
{
    public static readonly IReadOnlyList<SecForm> All =
    [
        new(DocumentType.TenK, SecFormFamily.Periodic),
        new(DocumentType.TenKa, SecFormFamily.Periodic, DocumentType.TenK),
        new(DocumentType.TenQ, SecFormFamily.Periodic),
        new(DocumentType.TenQa, SecFormFamily.Periodic, DocumentType.TenQ),
        new(DocumentType.TwentyF, SecFormFamily.Periodic),
        new(DocumentType.TwentyFa, SecFormFamily.Periodic, DocumentType.TwentyF),
        new(DocumentType.FortyF, SecFormFamily.Periodic),
        new(DocumentType.FortyFa, SecFormFamily.Periodic, DocumentType.FortyF),
        new(DocumentType.EightK, SecFormFamily.Current),
        new(DocumentType.EightKa, SecFormFamily.Current, DocumentType.EightK),
        new(DocumentType.SixK, SecFormFamily.Current),
        new(DocumentType.SixKa, SecFormFamily.Current, DocumentType.SixK),
        new(DocumentType.Def14A, SecFormFamily.Proxy),
        new(DocumentType.Defa14A, SecFormFamily.Proxy),
        new(DocumentType.Defm14A, SecFormFamily.Proxy),
        new(DocumentType.Defr14A, SecFormFamily.Proxy),
        new(DocumentType.Pre14A, SecFormFamily.Proxy),
        new(DocumentType.Prem14A, SecFormFamily.Proxy),
        // A registration amendment re-files the whole prospectus, so syncing every one stores
        // near-duplicate copies that crowd an issuer's search results; deployments can opt in.
        new(DocumentType.SOne, SecFormFamily.Registration),
        new(
            DocumentType.SOneA,
            SecFormFamily.Registration,
            DocumentType.SOne,
            SyncedByDefault: false
        ),
        new(DocumentType.SThree, SecFormFamily.Registration),
        new(
            DocumentType.SThreeA,
            SecFormFamily.Registration,
            DocumentType.SThree,
            SyncedByDefault: false
        ),
        new(DocumentType.SFour, SecFormFamily.Registration),
        new(
            DocumentType.SFourA,
            SecFormFamily.Registration,
            DocumentType.SFour,
            SyncedByDefault: false
        ),
        new(DocumentType.FOne, SecFormFamily.Registration),
        new(
            DocumentType.FOneA,
            SecFormFamily.Registration,
            DocumentType.FOne,
            SyncedByDefault: false
        ),
        new(DocumentType.FThree, SecFormFamily.Registration),
        new(
            DocumentType.FThreeA,
            SecFormFamily.Registration,
            DocumentType.FThree,
            SyncedByDefault: false
        ),
        new(DocumentType.FFour, SecFormFamily.Registration),
        new(
            DocumentType.FFourA,
            SecFormFamily.Registration,
            DocumentType.FFour,
            SyncedByDefault: false
        ),
        new(DocumentType.TenderOfferThirdParty, SecFormFamily.TenderOffer),
        new(
            DocumentType.TenderOfferThirdPartyA,
            SecFormFamily.TenderOffer,
            DocumentType.TenderOfferThirdParty
        ),
        new(DocumentType.TenderOfferIssuer, SecFormFamily.TenderOffer),
        new(
            DocumentType.TenderOfferIssuerA,
            SecFormFamily.TenderOffer,
            DocumentType.TenderOfferIssuer
        ),
        new(DocumentType.TenderOfferCommunication, SecFormFamily.TenderOffer),
        new(DocumentType.TenderOfferResponse, SecFormFamily.TenderOffer),
        new(
            DocumentType.TenderOfferResponseA,
            SecFormFamily.TenderOffer,
            DocumentType.TenderOfferResponse
        ),
        new(DocumentType.LateTenK, SecFormFamily.LateFilingNotice),
        new(DocumentType.LateTenQ, SecFormFamily.LateFilingNotice),
        new(DocumentType.LateTwentyF, SecFormFamily.LateFilingNotice),
        new(DocumentType.AnnualReportToHolders, SecFormFamily.AnnualReportToHolders),
        new(DocumentType.FormThree, SecFormFamily.Ownership),
        new(DocumentType.FormThreeA, SecFormFamily.Ownership, DocumentType.FormThree),
        new(DocumentType.FormFour, SecFormFamily.Ownership),
        new(DocumentType.FormFourA, SecFormFamily.Ownership, DocumentType.FormFour),
        new(DocumentType.FormFive, SecFormFamily.Ownership),
        new(DocumentType.FormFiveA, SecFormFamily.Ownership, DocumentType.FormFive),
        new(DocumentType.Form144, SecFormFamily.Ownership),
        new(DocumentType.FormD, SecFormFamily.Offering),
        new(DocumentType.FormDa, SecFormFamily.Offering, DocumentType.FormD),
        new(DocumentType.NCen, SecFormFamily.Fund),
        new(DocumentType.NCenA, SecFormFamily.Fund, DocumentType.NCen),
        new(DocumentType.NportP, SecFormFamily.Fund),
        new(DocumentType.NportPa, SecFormFamily.Fund, DocumentType.NportP),
    ];

    private static readonly Dictionary<DocumentType, SecForm> ByType = All.ToDictionary(f =>
        f.Type
    );

    /// <summary>The catalog entry for a document type, or null when the type is not an SEC form.</summary>
    public static SecForm Find(DocumentType type) =>
        type == null ? null : ByType.GetValueOrDefault(type);

    public static bool IsEdgar(DocumentType type) => Find(type) != null;

    /// <summary>The catalog entry for an EDGAR form string, or null for a form the platform does not recognize.</summary>
    public static SecForm ForEdgarForm(string edgarForm) =>
        Find(DocumentType.FromDisplayName(edgarForm));

    /// <summary>The document types the filing scraper syncs unless configuration overrides the list.</summary>
    public static IEnumerable<DocumentType> SyncedByDefault =>
        All.Where(f => f.SyncedByDefault).Select(f => f.Type);
}
