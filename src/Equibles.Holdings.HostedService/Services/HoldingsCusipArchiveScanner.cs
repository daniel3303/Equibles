using System.IO.Compression;
using System.Runtime.CompilerServices;
using static Equibles.Holdings.HostedService.Services.HoldingsParsingHelper;

namespace Equibles.Holdings.HostedService.Services;

internal static class HoldingsCusipArchiveScanner
{
    internal sealed record Filing(string AccessionNumber, string Cik, DateOnly FilingDate);

    internal static async Task<IReadOnlyList<Filing>> FindAffectedFilers(
        ZipArchive archive,
        HashSet<string> cusips,
        DateOnly minReportDate,
        CancellationToken cancellationToken
    )
    {
        // Read the original source, before amendment deduplication and CUSIP mapping:
        // an unresolved position has no stored holding from which to discover its filer.
        var accessions = new HashSet<string>(StringComparer.Ordinal);
        await foreach (
            var row in Read(
                archive,
                "INFOTABLE.tsv",
                ["ACCESSION_NUMBER", "CUSIP"],
                cancellationToken
            )
        )
        {
            if (cusips.Contains(GetValue(row, "CUSIP")))
            {
                var accession = GetValue(row, "ACCESSION_NUMBER");
                if (string.IsNullOrWhiteSpace(accession))
                    throw new InvalidDataException("A matching CUSIP has no accession number.");
                accessions.Add(accession);
            }
        }
        var found = new Dictionary<string, Filing>(StringComparer.Ordinal);
        await foreach (
            var row in Read(
                archive,
                "SUBMISSION.tsv",
                ["ACCESSION_NUMBER", "CIK", "FILING_DATE", "PERIODOFREPORT", "SUBMISSIONTYPE"],
                cancellationToken
            )
        )
        {
            var accession = GetValue(row, "ACCESSION_NUMBER");
            if (!accessions.Contains(accession))
                continue;
            var form = GetValue(row, "SUBMISSIONTYPE");
            if (form is not ("13F-HR" or "13F-HR/A"))
                throw new InvalidDataException(
                    $"Unsupported matched submission {accession}: {form}."
                );
            var cik = NormalizeCik(GetValue(row, "CIK"));
            if (
                cik == null
                || !TryParseDateOnly(GetValue(row, "FILING_DATE"), out var filed)
                || !TryParseDateOnly(GetValue(row, "PERIODOFREPORT"), out var report)
            )
                throw new InvalidDataException($"Incomplete matched submission {accession}.");
            accessions.Remove(accession);
            if (report < minReportDate)
                continue;
            var candidate = new Filing(accession, cik, filed);
            if (
                !found.TryGetValue(cik, out var previous)
                || filed < previous.FilingDate
                || (
                    filed == previous.FilingDate
                    && StringComparer.Ordinal.Compare(accession, previous.AccessionNumber) < 0
                )
            )
                found[cik] = candidate;
        }
        if (accessions.Count != 0)
            throw new InvalidDataException("Matched accessions are missing from SUBMISSION.tsv.");
        return found
            .Values.OrderBy(row => row.FilingDate)
            .ThenBy(row => row.AccessionNumber, StringComparer.Ordinal)
            .ToList();
    }

    private static async IAsyncEnumerable<Dictionary<string, string>> Read(
        ZipArchive archive,
        string name,
        string[] required,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        var entry = FindEntry(archive, name) ?? throw new InvalidDataException($"Missing {name}.");
        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var header = await reader.ReadLineAsync(cancellationToken);
        var columns = header?.Split('\t').Select(column => column.Trim()).ToArray() ?? [];
        if (required.Any(column => !columns.Contains(column, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException($"Missing required columns in {name}.");
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var values = line.Split('\t');
            if (values.Length != columns.Length)
                throw new InvalidDataException($"Malformed row in {name}.");
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columns.Length; i++)
                row[columns[i]] = values[i].Trim();
            yield return row;
        }
    }
}
