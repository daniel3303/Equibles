namespace Equibles.Congress.Data.Models;

/// <summary>
/// House periodic transaction reports store the Clerk's two-letter asset type code ("ST"), while
/// Senate reports store a name ("Stock"); readers show the House code under the Clerk's published
/// name (fd.house.gov/reference/asset-type-codes.aspx) so both chambers read in words.
/// </summary>
public static class HouseAssetTypeCodes
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["4K"] = "401K and Other Non-Federal Retirement Accounts",
        ["5C"] = "529 College Savings Plan",
        ["5F"] = "529 Portfolio",
        ["5P"] = "529 Prepaid Tuition Plan",
        ["AB"] = "Asset-Backed Securities",
        ["BA"] = "Bank Accounts, Money Market Accounts and CDs",
        ["BK"] = "Brokerage Accounts",
        ["CO"] = "Collectibles",
        ["CS"] = "Corporate Securities (Bonds and Notes)",
        ["CT"] = "Cryptocurrency",
        ["DB"] = "Defined Benefit Pension",
        ["DO"] = "Debts Owed to the Filer",
        ["DS"] = "Delaware Statutory Trust",
        ["EF"] = "Exchange Traded Funds (ETF)",
        ["EQ"] = "Excepted/Qualified Blind Trust",
        ["ET"] = "Exchange Traded Notes",
        ["FA"] = "Farms",
        ["FE"] = "Foreign Exchange Position (Currency)",
        ["FN"] = "Fixed Annuity",
        ["FU"] = "Futures",
        ["GS"] = "Government Securities and Agency Debt",
        ["HE"] = "Hedge Funds & Private Equity Funds (EIF)",
        ["HN"] = "Hedge Funds & Private Equity Funds (non-EIF)",
        ["IC"] = "Investment Club",
        ["IH"] = "IRA (Held in Cash)",
        ["IP"] = "Intellectual Property & Royalties",
        ["IR"] = "IRA",
        ["MA"] = "Managed Accounts (e.g., SMA and UMA)",
        ["MF"] = "Mutual Funds",
        ["MO"] = "Mineral/Oil/Solar Energy Rights",
        ["OI"] = "Ownership Interest (Holding Investments)",
        ["OL"] = "Ownership Interest (Engaged in a Trade or Business)",
        ["OP"] = "Options",
        ["OT"] = "Other",
        ["PE"] = "Pensions",
        ["PM"] = "Precious Metals",
        ["PS"] = "Stock (Not Publicly Traded)",
        ["RE"] = "Real Estate Invest. Trust (REIT)",
        ["RF"] = "REIT (EIF)",
        ["RN"] = "REIT (non-EIF)",
        ["RP"] = "Real Property",
        ["RS"] = "Restricted Stock Units (RSUs)",
        ["SA"] = "Stock Appreciation Right",
        ["ST"] = "Stocks (including ADRs)",
        ["TR"] = "Trust",
        ["VA"] = "Variable Annuity",
        ["VI"] = "Variable Insurance",
        ["WU"] = "Whole/Universal Insurance",
    };

    // The stored value when it is not a House code (a Senate name, blank, or a future code).
    public static string Display(string assetType) =>
        assetType != null && Names.TryGetValue(assetType.Trim(), out var name) ? name : assetType;
}
