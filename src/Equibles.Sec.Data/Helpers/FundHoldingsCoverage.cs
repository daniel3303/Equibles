namespace Equibles.Sec.Data.Helpers;

public static class FundHoldingsCoverage
{
    public const string FullPortfolio = "fullPortfolio";
    public const string TrackedEquitiesOnly = "trackedEquitiesOnly";
    public const string Unknown = "unknown";

    public static string Classify(int? storedCount, int? reportedCount)
    {
        if (!storedCount.HasValue || !reportedCount.HasValue)
            return Unknown;
        if (storedCount.Value == reportedCount.Value)
            return FullPortfolio;
        return storedCount.Value < reportedCount.Value ? TrackedEquitiesOnly : Unknown;
    }
}
