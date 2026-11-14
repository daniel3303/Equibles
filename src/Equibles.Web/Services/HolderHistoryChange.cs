using Equibles.Holdings.Data.Models;

namespace Equibles.Web.Services;

/// <summary>
/// Quarter-over-quarter share change of each row in one holder's history of one stock. A row is
/// compared with the same line (listed series, share type, option type) in the nearest earlier
/// quarter that reported it, never with whichever row sits next in the list: a quarter can carry
/// shares, calls and puts side by side.
/// </summary>
public static class HolderHistoryChange
{
    /// <param name="history">Rows already restated onto one split basis.</param>
    public static Dictionary<Guid, double> PercentByRow(
        IReadOnlyCollection<InstitutionalHolding> history
    )
    {
        var changes = new Dictionary<Guid, double>();
        foreach (var line in history.GroupBy(h => (h.ListedTicker, h.ShareType, h.OptionType)))
        {
            var ordered = line.OrderBy(h => h.ReportDate).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                var older = ordered[i - 1];
                if (older.Shares > 0)
                    changes[ordered[i].Id] =
                        (double)(ordered[i].Shares - older.Shares) / older.Shares * 100;
            }
        }
        return changes;
    }
}
