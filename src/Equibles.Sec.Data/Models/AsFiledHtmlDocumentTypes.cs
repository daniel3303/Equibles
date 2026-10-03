namespace Equibles.Sec.Data.Models;

public static class AsFiledHtmlDocumentTypes
{
    public static IReadOnlyList<DocumentType> Periodic { get; } = Array.AsReadOnly<DocumentType>(
        [
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
        ]
    );

    public static IReadOnlyList<DocumentType> Supported { get; } = Array.AsReadOnly<DocumentType>(
        [DocumentType.EightK, DocumentType.EightKa, .. Periodic]
    );
}
