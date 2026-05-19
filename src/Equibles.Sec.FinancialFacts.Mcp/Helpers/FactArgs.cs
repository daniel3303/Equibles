using Equibles.Sec.FinancialFacts.Data.Enums;

namespace Equibles.Sec.FinancialFacts.Mcp.Helpers;

/// <summary>
/// Shared parsing of free-text MCP tool arguments into FinancialFacts domain
/// types, so every tool accepts the same spellings.
/// </summary>
public static class FactArgs
{
    public static bool TryParsePeriod(string value, out SecFiscalPeriod period)
    {
        switch (value?.Trim().ToUpperInvariant())
        {
            case "FY":
            case "FULLYEAR":
            case "ANNUAL":
                period = SecFiscalPeriod.FullYear;
                return true;
            case "Q1":
                period = SecFiscalPeriod.Q1;
                return true;
            case "Q2":
                period = SecFiscalPeriod.Q2;
                return true;
            case "Q3":
                period = SecFiscalPeriod.Q3;
                return true;
            case "Q4":
                period = SecFiscalPeriod.Q4;
                return true;
            default:
                period = default;
                return false;
        }
    }
}
