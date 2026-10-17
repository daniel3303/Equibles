namespace Equibles.Holdings.Data.Models;

/// <summary>Maps SEC's two-letter reporting-person codes to <see cref="ReportingPersonTypes"/>.</summary>
public static class ReportingPersonTypeCodes
{
    private static readonly IReadOnlyDictionary<string, ReportingPersonTypes> ByCode =
        new Dictionary<string, ReportingPersonTypes>(StringComparer.OrdinalIgnoreCase)
        {
            ["BD"] = ReportingPersonTypes.BrokerDealer,
            ["BK"] = ReportingPersonTypes.Bank,
            ["IC"] = ReportingPersonTypes.InsuranceCompany,
            ["IV"] = ReportingPersonTypes.InvestmentCompany,
            ["IA"] = ReportingPersonTypes.InvestmentAdviser,
            ["EP"] = ReportingPersonTypes.EmployeeBenefitPlan,
            ["HC"] = ReportingPersonTypes.ParentHoldingCompany,
            ["SA"] = ReportingPersonTypes.SavingsAssociation,
            ["CP"] = ReportingPersonTypes.ChurchPlan,
            ["CO"] = ReportingPersonTypes.Corporation,
            ["PN"] = ReportingPersonTypes.Partnership,
            ["IN"] = ReportingPersonTypes.Individual,
            ["FI"] = ReportingPersonTypes.NonUsInstitution,
            ["OO"] = ReportingPersonTypes.Other,
        };

    /// <summary>
    /// Combines the stated codes; codes outside SEC's list come back in
    /// <paramref name="unrecognized"/> (comma-separated, null when none) instead of being dropped.
    /// </summary>
    public static ReportingPersonTypes Parse(IEnumerable<string> codes, out string unrecognized)
    {
        var types = ReportingPersonTypes.None;
        var unknown = new List<string>();
        foreach (
            var code in (codes ?? [])
                .Select(raw => raw?.Trim())
                .Where(code => !string.IsNullOrEmpty(code))
        )
        {
            if (ByCode.TryGetValue(code, out var type))
                types |= type;
            else if (!unknown.Contains(code, StringComparer.OrdinalIgnoreCase))
                unknown.Add(code);
        }

        unrecognized = unknown.Count == 0 ? null : string.Join(",", unknown);
        return types;
    }
}
