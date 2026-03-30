using Equibles.Sec.Data.Models;

namespace Equibles.Sec.HostedService.Configuration;

public class DocumentScraperOptions {
    public List<string> DocumentTypesToSync { get; set; } = [
        "TenK", "TenQ", "EightK", "FormFour", "FormThree"
    ];

    private List<DocumentType> _resolvedTypes;

    public List<DocumentType> GetDocumentTypes() {
        return _resolvedTypes ??= DocumentTypesToSync
            .Select(DocumentType.FromValue)
            .Where(t => t != null)
            .ToList();
    }
}
