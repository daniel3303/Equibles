using Equibles.Sec.Data.Models;
using Equibles.Integrations.Sec.Models;

namespace Equibles.Sec.HostedService.Extensions;

public static class DocumentTypeExtensions {
    private static readonly Dictionary<DocumentType, DocumentTypeFilter> DatabaseToSecMapping = new() {
        { DocumentType.TenK, DocumentTypeFilter.TenK },
        { DocumentType.TenQ, DocumentTypeFilter.TenQ },
        { DocumentType.TenKa, DocumentTypeFilter.TenKa },
        { DocumentType.TenQa, DocumentTypeFilter.TenQa },
        { DocumentType.EightK, DocumentTypeFilter.EightK },
        { DocumentType.EightKa, DocumentTypeFilter.EightKa },
        { DocumentType.TwentyF, DocumentTypeFilter.TwentyF },
        { DocumentType.SixK, DocumentTypeFilter.SixK },
        { DocumentType.FortyF, DocumentTypeFilter.FortyF },
        { DocumentType.FormFour, DocumentTypeFilter.FormFour },
        { DocumentType.FormThree, DocumentTypeFilter.FormThree }
    };

    public static DocumentTypeFilter? ToSecEdgarFilter(this DocumentType docType) {
        return DatabaseToSecMapping.TryGetValue(docType, out var filter) ? filter : null;
    }

    public static DocumentType FromFormName(string formName) {
        return DocumentType.FromDisplayName(formName);
    }
}
