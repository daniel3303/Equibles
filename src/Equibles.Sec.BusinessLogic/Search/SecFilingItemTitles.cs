namespace Equibles.Sec.BusinessLogic.Search;

/// <summary>
/// Plain-language labels for the SEC Form 8-K item numbers (General Instructions B and the
/// Form 8-K item list), so a filing can be described by what it reports when no other
/// description exists.
/// </summary>
public static class SecFilingItemTitles
{
    // Item 9.01 accompanies most reports and says nothing about the event itself.
    private const string ExhibitsItem = "9.01";
    private const int MaxDescribedItems = 3;

    private static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<
        string,
        string
    >
    {
        ["1.01"] = "Entry into a material definitive agreement",
        ["1.02"] = "Termination of a material definitive agreement",
        ["1.03"] = "Bankruptcy or receivership",
        ["1.04"] = "Mine safety shutdowns and violations",
        ["1.05"] = "Material cybersecurity incident",
        ["2.01"] = "Completed acquisition or disposition of assets",
        ["2.02"] = "Results of operations and financial condition",
        ["2.03"] = "New direct financial obligation",
        ["2.04"] = "Event accelerating a financial obligation",
        ["2.05"] = "Costs of exit or disposal activities",
        ["2.06"] = "Material impairment",
        ["3.01"] = "Delisting notice or listing transfer",
        ["3.02"] = "Unregistered sale of equity securities",
        ["3.03"] = "Material change to security holders' rights",
        ["4.01"] = "Change of certifying accountant",
        ["4.02"] = "Non-reliance on previously issued financial statements",
        ["5.01"] = "Change in control",
        ["5.02"] = "Director or officer departure, election or appointment",
        ["5.03"] = "Amendment to articles or bylaws, or fiscal year change",
        ["5.04"] = "Suspension of trading under employee benefit plans",
        ["5.05"] = "Code of ethics amendment or waiver",
        ["5.06"] = "Change in shell company status",
        ["5.07"] = "Shareholder vote results",
        ["5.08"] = "Shareholder director nominations",
        ["6.01"] = "Asset-backed securities informational material",
        ["6.02"] = "Change of servicer or trustee",
        ["6.03"] = "Change in credit enhancement",
        ["6.04"] = "Failure to make a required distribution",
        ["6.05"] = "Securities Act updating disclosure",
        ["6.06"] = "Static pool information",
        ["6.10"] = "Alternative filing of an asset-backed issuer",
        ["7.01"] = "Regulation FD disclosure",
        ["8.01"] = "Other events",
        ["9.01"] = "Financial statements and exhibits",
    };

    /// <summary>
    /// The label for one item number, or null for a number outside the Form 8-K item list.
    /// </summary>
    public static string Label(string itemNumber)
    {
        var normalized = SecFilingItemNumber.Normalize(itemNumber);
        return normalized != null && Labels.TryGetValue(normalized, out var label) ? label : null;
    }

    /// <summary>
    /// Describes a filing's stored item list (comma-joined, as on <c>Document.Items</c>), such as
    /// "Results of operations and financial condition (Item 2.02); Regulation FD disclosure
    /// (Item 7.01)". Item 9.01 is left out unless it is the only item, unknown numbers are
    /// skipped, and at most three items are named. Null when no item can be named.
    /// </summary>
    public static string Describe(string storedItems)
    {
        var known = SecFilingItemNumber
            .ParseStored(storedItems)
            .Select(SecFilingItemNumber.Normalize)
            .Where(item => item != null && Labels.ContainsKey(item))
            .Distinct()
            .ToList();
        if (known.Count == 0)
            return null;

        var substantive = known.Where(item => item != ExhibitsItem).ToList();
        var named = (substantive.Count > 0 ? substantive : known).Take(MaxDescribedItems);
        return string.Join("; ", named.Select(item => $"{Labels[item]} (Item {item})"));
    }
}
