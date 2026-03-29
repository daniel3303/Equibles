using System.IO.Compression;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;

namespace Equibles.Holdings.HostedService.Services;

internal static class HoldingsParsingHelper {
    internal static ZipArchiveEntry FindEntry(ZipArchive archive, string fileName) {
        return archive.GetEntry(fileName)
            ?? archive.Entries.FirstOrDefault(e => e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }

    internal static string GetValue(Dictionary<string, string> row, string key) {
        return row.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Parses date strings in both ISO (yyyy-MM-dd) and SEC (dd-MMM-yyyy) formats.
    /// </summary>
    internal static bool TryParseDateOnly(string value, out DateOnly result) {
        result = default;
        if (string.IsNullOrEmpty(value)) return false;

        if (DateOnly.TryParse(value, out result)) return true;

        if (DateOnly.TryParseExact(value, "dd-MMM-yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out result)) {
            return true;
        }

        return false;
    }

    internal static long ParseLong(string value) {
        return long.TryParse(value, out var result) ? result : 0;
    }

    internal static ShareType ParseShareType(string value) {
        return value?.ToUpperInvariant() switch {
            "SH" => ShareType.Shares,
            "PRN" => ShareType.Principal,
            _ => ShareType.Shares,
        };
    }

    internal static Equibles.Holdings.Data.Models.OptionType? ParseOptionType(string value) {
        return value?.ToUpperInvariant() switch {
            "PUT" => Equibles.Holdings.Data.Models.OptionType.Put,
            "CALL" => Equibles.Holdings.Data.Models.OptionType.Call,
            _ => null,
        };
    }

    internal static int? ParseNullableInt(string value) {
        return int.TryParse(value, out var result) ? result : null;
    }

    internal static string ResolveManagerName(ImportContext context, string accession, int? managerNumber) {
        if (managerNumber == null) return null;
        if (context.OtherManagers.TryGetValue(accession, out var seqMap)
            && seqMap.TryGetValue(managerNumber.Value, out var name)) {
            return name;
        }
        return null;
    }

    internal static InvestmentDiscretion ParseInvestmentDiscretion(string value) {
        return value?.ToUpperInvariant() switch {
            "SOLE" => InvestmentDiscretion.Sole,
            "DFND" => InvestmentDiscretion.Defined,
            "OTR" => InvestmentDiscretion.Other,
            _ => InvestmentDiscretion.Sole,
        };
    }
}
