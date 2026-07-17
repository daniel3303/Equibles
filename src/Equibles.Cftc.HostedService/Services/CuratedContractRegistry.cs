using Equibles.Cftc.Data.Models;

namespace Equibles.Cftc.HostedService.Services;

// Display names MUST match what the CFTC assigns to each contract market code — the code
// is the identity the importer filters the COT files by, so a wrong name silently labels
// one market's real data as another market. Every code below was verified against the
// CFTC public reporting API (publicreporting.cftc.gov, legacy futures-only dataset
// 6dca-aqww, field cftc_contract_market_code → market_and_exchange_names). An earlier
// revision of this registry carried ten mislabeled codes (e.g. 057642 said "Lean Hogs"
// while the CFTC assigns it to Live Cattle; 075651/076651 had Platinum and Palladium
// swapped); CftcImportService.EnsureContractsExist re-syncs drifted names on boot.
public static class CuratedContractRegistry
{
    public static readonly IReadOnlyList<CuratedContract> Contracts =
    [
        // Agriculture
        new("001602", "Wheat-SRW (CBOT)", CftcContractCategory.Agriculture),
        new("002602", "Corn (CBOT)", CftcContractCategory.Agriculture),
        new("005602", "Soybeans (CBOT)", CftcContractCategory.Agriculture),
        new("033661", "Cotton No. 2 (ICE)", CftcContractCategory.Agriculture),
        new("073732", "Cocoa (ICE)", CftcContractCategory.Agriculture),
        new("083731", "Coffee C (ICE)", CftcContractCategory.Agriculture),
        new("080732", "Sugar No. 11 (ICE)", CftcContractCategory.Agriculture),
        new("054642", "Lean Hogs (CME)", CftcContractCategory.Agriculture),
        new("057642", "Live Cattle (CME)", CftcContractCategory.Agriculture),
        new("061641", "Feeder Cattle (CME)", CftcContractCategory.Agriculture),
        // Energy
        new("067651", "Crude Oil, Light Sweet (NYMEX)", CftcContractCategory.Energy),
        new("023651", "Natural Gas (NYMEX)", CftcContractCategory.Energy),
        new("022651", "No. 2 Heating Oil (NYMEX)", CftcContractCategory.Energy),
        new("111659", "RBOB Gasoline (NYMEX)", CftcContractCategory.Energy),
        // Metals
        new("088691", "Gold (COMEX)", CftcContractCategory.Metals),
        new("084691", "Silver (COMEX)", CftcContractCategory.Metals),
        new("085692", "Copper Grade #1 (COMEX)", CftcContractCategory.Metals),
        new("076651", "Platinum (NYMEX)", CftcContractCategory.Metals),
        new("075651", "Palladium (NYMEX)", CftcContractCategory.Metals),
        // Equity Indices
        new("13874A", "E-mini S&P 500 (CME)", CftcContractCategory.EquityIndices),
        new("209742", "E-mini Nasdaq-100 (CME)", CftcContractCategory.EquityIndices),
        new("124603", "E-mini Dow (CBOT)", CftcContractCategory.EquityIndices),
        new("239742", "E-mini Russell 2000 (CME)", CftcContractCategory.EquityIndices),
        new("1170E1", "VIX Futures (CBOE)", CftcContractCategory.EquityIndices),
        // Interest Rates
        new("043602", "10-Year T-Notes (CBOT)", CftcContractCategory.InterestRates),
        new("020601", "30-Year T-Bonds (CBOT)", CftcContractCategory.InterestRates),
        new("044601", "5-Year T-Notes (CBOT)", CftcContractCategory.InterestRates),
        new("042601", "2-Year T-Notes (CBOT)", CftcContractCategory.InterestRates),
        // Currencies
        new("099741", "Euro FX (CME)", CftcContractCategory.Currencies),
        new("097741", "Japanese Yen (CME)", CftcContractCategory.Currencies),
        new("096742", "British Pound (CME)", CftcContractCategory.Currencies),
        new("090741", "Canadian Dollar (CME)", CftcContractCategory.Currencies),
        new("092741", "Swiss Franc (CME)", CftcContractCategory.Currencies),
        new("095741", "Mexican Peso (CME)", CftcContractCategory.Currencies),
        new("232741", "Australian Dollar (CME)", CftcContractCategory.Currencies),
    ];
}

public record CuratedContract(string MarketCode, string DisplayName, CftcContractCategory Category);
