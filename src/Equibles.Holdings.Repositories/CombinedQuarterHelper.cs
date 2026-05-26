namespace Equibles.Holdings.Repositories;

public static class CombinedQuarterHelper
{
    public const int FilingDeadlineDays = 45;

    public static bool IsFilingWindowOpen(DateOnly latestReportDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return today <= latestReportDate.AddDays(FilingDeadlineDays);
    }
}
