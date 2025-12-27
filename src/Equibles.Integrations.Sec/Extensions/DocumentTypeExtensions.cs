using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Equibles.Integrations.Sec.Models;

namespace Equibles.Integrations.Sec.Extensions;

internal static class DocumentTypeExtensions {
    public static string GetFormName(this DocumentTypeFilter documentType) {
        var field = documentType.GetType().GetField(documentType.ToString());
        var attribute = field?.GetCustomAttribute<DisplayAttribute>();
        return attribute?.Name ?? documentType.ToString();
    }
}