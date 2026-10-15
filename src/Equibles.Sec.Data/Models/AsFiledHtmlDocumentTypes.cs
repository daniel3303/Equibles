namespace Equibles.Sec.Data.Models;

public static class AsFiledHtmlDocumentTypes
{
    public static IReadOnlyList<DocumentType> Periodic { get; } =
        Array.AsReadOnly<DocumentType>([
            DocumentType.TenK,
            DocumentType.TenKa,
            DocumentType.TenQ,
            DocumentType.TenQa,
            DocumentType.TwentyF,
            DocumentType.TwentyFa,
            DocumentType.FortyF,
            DocumentType.FortyFa,
            DocumentType.SixK,
            DocumentType.SixKa,
        ]);

    // Forms whose primary alone earns an as-filed page: periodic reports, and forms whose primary
    // carries no inline XBRL original of its own (an ARS filed as HTML rather than PDF among them).
    // An 8-K's inline primary already serves its readers;
    // DEF 14A keeps its inline original, since adding it would re-capture every stored proxy.
    public static IReadOnlyList<DocumentType> Standalone { get; } =
        Array.AsReadOnly<DocumentType>([
            .. Periodic,
            .. SecForms
                .All.Where(form =>
                    form.Family
                        is SecFormFamily.Proxy
                            or SecFormFamily.Registration
                            or SecFormFamily.TenderOffer
                            or SecFormFamily.LateFilingNotice
                            or SecFormFamily.AnnualReportToHolders
                )
                .Select(form => form.Type)
                .Where(type => type != DocumentType.Def14A),
        ]);

    public static IReadOnlyList<DocumentType> Supported { get; } =
        Array.AsReadOnly<DocumentType>([DocumentType.EightK, DocumentType.EightKa, .. Standalone]);
}
