using Equibles.Sec.Data.Models;

namespace Equibles.Sec.HostedService.Configuration;

public class DocumentScraperOptions
{
    // Amendments are first-class: 10-K/A, 10-Q/A and 8-K/A store as their own
    // document types, and Form 4/A / 3/A supersede their originals' transactions
    // in the insider pipeline. Omitting them left corrected filings invisible
    // while the erroneous originals stayed live.
    public List<DocumentType> DocumentTypesToSync { get; set; } =
    [
        DocumentType.TenK,
        DocumentType.TenQ,
        DocumentType.EightK,
        DocumentType.TenKa,
        DocumentType.TenQa,
        DocumentType.EightKa,
        DocumentType.FormFour,
        DocumentType.FormThree,
        DocumentType.FormFourA,
        DocumentType.FormThreeA,
        DocumentType.Form144,
        DocumentType.FormD,
        DocumentType.FormDa,
        DocumentType.NCen,
        DocumentType.NCenA,
        DocumentType.NportP,
        DocumentType.NportPa,
        DocumentType.Def14A,
    ];
}
