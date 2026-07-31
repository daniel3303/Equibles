using System.Globalization;
using System.IO.Compression;
using Equibles.Holdings.Data.Models;
using Equibles.Holdings.HostedService.Models;

namespace Equibles.Holdings.HostedService.Services;

internal static class HoldingsParsingHelper
{
    internal static ZipArchiveEntry FindEntry(ZipArchive archive, string fileName)
    {
        return archive.GetEntry(fileName)
            ?? archive.Entries.FirstOrDefault(e =>
                e.Name.Equals(fileName, StringComparison.OrdinalIgnoreCase)
            );
    }

    internal static string GetValue(Dictionary<string, string> row, string key)
    {
        return row.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Parses date strings in both ISO (yyyy-MM-dd) and SEC (dd-MMM-yyyy) formats.
    /// </summary>
    internal static bool TryParseDateOnly(string value, out DateOnly result)
    {
        result = default;
        if (string.IsNullOrEmpty(value))
            return false;

        // ISO/SEC dates are culture-invariant. Parsing with the host culture
        // breaks on non-Gregorian calendars (e.g. ar-SA Umm al-Qura), where a
        // Worker would reinterpret "2024-03-15" as a Hijri date.
        if (
            DateOnly.TryParse(
                value,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out result
            )
        )
            return true;

        if (
            DateOnly.TryParseExact(
                value,
                "dd-MMM-yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out result
            )
        )
        {
            return true;
        }

        return false;
    }

    internal static long ParseLong(string value)
    {
        return long.TryParse(value, out var result) ? result : 0;
    }

    internal static ShareType ParseShareType(string value)
    {
        return value?.ToUpperInvariant() switch
        {
            "SH" => ShareType.Shares,
            "PRN" => ShareType.Principal,
            _ => ShareType.Shares,
        };
    }

    internal static Equibles.Holdings.Data.Models.OptionType? ParseOptionType(string value)
    {
        return value?.ToUpperInvariant() switch
        {
            "PUT" => Equibles.Holdings.Data.Models.OptionType.Put,
            "CALL" => Equibles.Holdings.Data.Models.OptionType.Call,
            _ => null,
        };
    }

    internal static int? ParseNullableInt(string value)
    {
        return int.TryParse(value, out var result) ? result : null;
    }

    /// <summary>
    /// Parses a surrogate key. Widened past <see cref="int"/> because the SEC declares these
    /// columns as NUMBER(38), so a value can legitimately outrun a 32-bit parse.
    /// </summary>
    internal static long? ParseNullableLong(string value)
    {
        return long.TryParse(value, out var result) ? result : null;
    }

    internal static decimal? ParseNullableDecimal(string value)
    {
        return decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var result
        )
            ? result
            : null;
    }

    internal static string ResolveManagerName(
        ImportContext context,
        string accession,
        int? managerNumber
    )
    {
        if (managerNumber == null)
            return null;
        if (
            context.OtherManagers.TryGetValue(accession, out var seqMap)
            && seqMap.TryGetValue(managerNumber.Value, out var identity)
        )
        {
            return identity.Name;
        }
        return null;
    }

    /// <summary>
    /// Strips the leading zeros EDGAR pads CIKs with, so a filed identifier compares equal to the
    /// spelling filer CIKs are stored under. Blank stays null: an absent identifier must not
    /// become an empty string that looks like a value.
    /// </summary>
    internal static string NormalizeCik(string cik)
    {
        var trimmed = NormalizeIdentifier(cik);
        return trimmed == null ? null : NormalizeIdentifier(trimmed.TrimStart('0'));
    }

    /// <summary>
    /// Normalizes a filed identifier to "value or absent". A blank is absent: the SEC keeps the
    /// column present and empty rather than omitting it, and an empty string is not a missing
    /// identifier — it is a value that compares equal to every other blank, so joining on it
    /// would collide every identifier-less manager into one.
    /// </summary>
    internal static string NormalizeIdentifier(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    internal static InvestmentDiscretion ParseInvestmentDiscretion(string value)
    {
        return value?.ToUpperInvariant() switch
        {
            "SOLE" => InvestmentDiscretion.Sole,
            "DFND" => InvestmentDiscretion.Defined,
            "OTR" => InvestmentDiscretion.Other,
            _ => InvestmentDiscretion.Sole,
        };
    }
}
