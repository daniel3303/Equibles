using Equibles.Sec.Data.Models;

namespace Equibles.Sec.Repositories.Extensions;

public static class DocumentItemQueryExtensions
{
    // Items is a comma-joined list, so the number matches as a whole entry, never as a substring
    // of another item.
    public static IQueryable<Document> ReportingItem(
        this IQueryable<Document> documents,
        string itemNumber
    )
    {
        var first = itemNumber + ",";
        var middle = "," + itemNumber + ",";
        var last = "," + itemNumber;
        return documents.Where(d =>
            d.Items == itemNumber
            || d.Items.StartsWith(first)
            || d.Items.Contains(middle)
            || d.Items.EndsWith(last)
        );
    }
}
