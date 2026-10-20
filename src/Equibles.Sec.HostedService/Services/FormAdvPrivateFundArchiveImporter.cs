using System.Globalization;
using System.IO.Compression;
using System.Text;
using Equibles.Core.AutoWiring;
using Equibles.Data;
using Equibles.Integrations.Sec;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.FormAdv;
using Equibles.Sec.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Imports Schedule D 7.B.(1) private funds from SEC's Form ADV filing archive, which ends with
/// filings submitted on 2024-12-31 (SEC publishes later Schedule D data only per firm). Each
/// fund row names only its filing, so the archive's base table supplies the filing's CRD and
/// submission date. Funds are summed by type per filing, and an adviser's filing is kept only
/// when its sums differ from its previous kept filing. Only the needed tables are read, with
/// byte-range requests, instead of the whole 1.1 GB archive.
/// </summary>
[Service]
public class FormAdvPrivateFundArchiveImporter
{
    /// <summary>Bump to re-import every archive part.</summary>
    internal const int ParserVersion = 1;

    internal static readonly DateOnly ArchiveEndDate = new(2024, 12, 31);

    internal static readonly string[] ArchiveUrls =
    [
        "https://www.sec.gov/files/adv-filing-data-20111105-20241231-part1.zip",
        "https://www.sec.gov/files/adv-filing-data-20111105-20241231-part2.zip",
    ];

    // The exempt part is small, so it goes first.
    private static readonly (
        FormAdvRegistration Registration,
        string FilingTable,
        string FundTable
    )[] Parts =
    [
        (FormAdvRegistration.ExemptReporting, "ERA_ADV_Base_", "ERA_Schedule_D_7B1_"),
        (FormAdvRegistration.Registered, "IA_ADV_Base_A_", "IA_Schedule_D_7B1_"),
    ];

    private static readonly Dictionary<string, FormAdvPrivateFundType> FundTypes = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["Hedge Fund"] = FormAdvPrivateFundType.Hedge,
        ["Private Equity Fund"] = FormAdvPrivateFundType.PrivateEquity,
        ["Venture Capital Fund"] = FormAdvPrivateFundType.VentureCapital,
        ["Real Estate Fund"] = FormAdvPrivateFundType.RealEstate,
        ["Liquidity Fund"] = FormAdvPrivateFundType.Liquidity,
        ["Securitized Asset Fund"] = FormAdvPrivateFundType.Securitized,
        ["Other Private Fund"] = FormAdvPrivateFundType.Other,
    };

    private const int WriteBatchSize = 5_000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly ILogger<FormAdvPrivateFundArchiveImporter> _logger;

    public FormAdvPrivateFundArchiveImporter(
        IServiceScopeFactory scopeFactory,
        ISecEdgarClient secEdgarClient,
        ILogger<FormAdvPrivateFundArchiveImporter> logger
    )
    {
        _scopeFactory = scopeFactory;
        _secEdgarClient = secEdgarClient;
        _logger = logger;
    }

    /// <summary>
    /// Imports the next archive part this version has not imported; returns true when another
    /// part remains after it.
    /// </summary>
    public async Task<bool> ImportNext(CancellationToken cancellationToken)
    {
        var pending = await PendingParts(cancellationToken);
        if (pending.Count == 0)
            return false;

        var (registration, filingTable, fundTable) = pending[0];
        var archives = new List<ZipArchive>();
        try
        {
            foreach (var url in ArchiveUrls)
                archives.Add(
                    new ZipArchive(
                        await SecRemoteFile.Open(
                            _secEdgarClient,
                            url,
                            cancellationToken: cancellationToken
                        ),
                        ZipArchiveMode.Read
                    )
                );
            Dictionary<int, FormAdvPrivateFundSums> funds;
            using (var reader = OpenTable(archives, fundTable))
                funds = ReadFunds(CsvRecordReader.Read(reader));
            List<FormAdvFilingRow> filings;
            using (var reader = OpenTable(archives, filingTable))
                filings = ReadFilings(CsvRecordReader.Read(reader), funds.Keys.ToHashSet());

            var rows = Collapse(filings, funds, registration);
            await Replace(registration, rows, cancellationToken);
            _logger.LogInformation(
                "Form ADV archive {Roster}: {Funds} filings list private funds, {Unmatched} without a filing record; kept {Kept} filings of {Advisers} advisers",
                registration,
                funds.Count,
                funds.Count - filings.Count(filing => funds.ContainsKey(filing.FilingId)),
                rows.Count,
                rows.Select(row => row.Crd).Distinct().Count()
            );
        }
        finally
        {
            foreach (var archive in archives)
                archive.Dispose();
        }
        return pending.Count > 1;
    }

    private async Task<
        List<(FormAdvRegistration Registration, string FilingTable, string FundTable)>
    > PendingParts(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var done = await dbContext
            .Set<FormAdvFilingArchiveImport>()
            .Where(import =>
                import.ArchiveEndDate == ArchiveEndDate && import.ParserVersion >= ParserVersion
            )
            .Select(import => import.Registration)
            .ToListAsync(cancellationToken);
        return Parts.Where(part => !done.Contains(part.Registration)).ToList();
    }

    // Tables are found by file name in either part; their folders differ between parts.
    private static StreamReader OpenTable(List<ZipArchive> archives, string prefix)
    {
        var entry = archives
            .SelectMany(archive => archive.Entries)
            .FirstOrDefault(entry =>
                entry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            );
        if (entry == null)
            throw new InvalidDataException(
                $"SEC's Form ADV filing archive has no {prefix.TrimEnd('_')} table."
            );
        return new StreamReader(entry.Open(), Encoding.Latin1);
    }

    /// <summary>Sums each filing's Schedule D 7.B.(1) funds by type.</summary>
    internal static Dictionary<int, FormAdvPrivateFundSums> ReadFunds(
        IEnumerable<List<string>> records
    )
    {
        var sums = new Dictionary<int, FormAdvPrivateFundSums>();
        int filingColumn = -1,
            typeColumn = -1,
            assetsColumn = -1;
        foreach (var record in records)
        {
            if (filingColumn < 0)
            {
                filingColumn = Column(record, "FilingID");
                typeColumn = Column(record, "Fund Type");
                assetsColumn = Column(record, "Gross Asset Value");
                continue;
            }
            if (!TryInt(Cell(record, filingColumn), out var filingId))
                continue;
            if (!sums.TryGetValue(filingId, out var filing))
                sums[filingId] = filing = new FormAdvPrivateFundSums();
            filing.Add(
                FundTypes.TryGetValue(Cell(record, typeColumn), out var type) ? type : null,
                ParseAssets(Cell(record, assetsColumn))
            );
        }
        return sums;
    }

    /// <summary>The CRD and submission date of every filing in <paramref name="filingIds"/>.</summary>
    internal static List<FormAdvFilingRow> ReadFilings(
        IEnumerable<List<string>> records,
        IReadOnlySet<int> filingIds
    )
    {
        var filings = new List<FormAdvFilingRow>();
        var all = new List<FormAdvFilingRow>();
        int filingColumn = -1,
            dateColumn = -1,
            crdColumn = -1;
        foreach (var record in records)
        {
            if (filingColumn < 0)
            {
                filingColumn = Column(record, "FilingID");
                dateColumn = Column(record, "DateSubmitted");
                crdColumn = Column(record, "1E1");
                continue;
            }
            if (
                !TryInt(Cell(record, filingColumn), out var filingId)
                || !TryInt(Cell(record, crdColumn), out var crd)
                || ParseSubmitted(Cell(record, dateColumn)) is not { } filedOn
            )
                continue;
            all.Add(new FormAdvFilingRow(filingId, crd, filedOn));
        }

        // Every filing of an adviser that ever listed funds, so a later filing without funds
        // can record that it stopped.
        var listing = all.Where(filing => filingIds.Contains(filing.FilingId))
            .Select(filing => filing.Crd)
            .ToHashSet();
        filings.AddRange(all.Where(filing => listing.Contains(filing.Crd)));
        return filings;
    }

    /// <summary>
    /// Each adviser's filings in submission order, kept only when their fund sums differ from the
    /// previous kept filing; a filing without funds is kept only after one with funds.
    /// </summary>
    internal static List<FormAdvPrivateFundFiling> Collapse(
        IEnumerable<FormAdvFilingRow> filings,
        IReadOnlyDictionary<int, FormAdvPrivateFundSums> funds,
        FormAdvRegistration registration
    )
    {
        var kept = new List<FormAdvPrivateFundFiling>();
        foreach (var adviser in filings.DistinctBy(filing => filing.FilingId).GroupBy(f => f.Crd))
        {
            FormAdvPrivateFundSums previous = null;
            foreach (
                var filing in adviser.OrderBy(filing => filing.FiledOn).ThenBy(f => f.FilingId)
            )
            {
                var current =
                    funds.GetValueOrDefault(filing.FilingId) ?? FormAdvPrivateFundSums.None;
                if (previous == null ? current.FundCount == 0 : current.SameAs(previous))
                    continue;
                kept.Add(current.ToFiling(filing, registration));
                previous = current;
            }
        }
        return kept;
    }

    private async Task Replace(
        FormAdvRegistration registration,
        List<FormAdvPrivateFundFiling> rows,
        CancellationToken cancellationToken
    )
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EquiblesFinancialDbContext>();
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                cancellationToken
            );
            await dbContext
                .Set<FormAdvPrivateFundFiling>()
                .Where(row => row.Registration == registration)
                .ExecuteDeleteAsync(cancellationToken);
            foreach (var batch in rows.Chunk(WriteBatchSize))
            {
                dbContext.Set<FormAdvPrivateFundFiling>().AddRange(batch);
                await dbContext.SaveChangesAsync(cancellationToken);
                dbContext.ChangeTracker.Clear();
            }

            var import = await dbContext
                .Set<FormAdvFilingArchiveImport>()
                .SingleOrDefaultAsync(
                    row => row.Registration == registration && row.ArchiveEndDate == ArchiveEndDate,
                    cancellationToken
                );
            if (import == null)
            {
                import = new FormAdvFilingArchiveImport
                {
                    Registration = registration,
                    ArchiveEndDate = ArchiveEndDate,
                };
                dbContext.Add(import);
            }
            import.ParserVersion = ParserVersion;
            import.FilingCount = rows.Count;
            import.ImportedAt = DateTime.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private static int Column(List<string> header, string name)
    {
        var index = header.FindIndex(cell =>
            string.Equals(cell.Trim(), name, StringComparison.OrdinalIgnoreCase)
        );
        if (index < 0)
            throw new InvalidDataException(
                $"SEC's Form ADV filing archive table has no {name} column."
            );
        return index;
    }

    private static string Cell(List<string> record, int column) =>
        column < record.Count ? record[column].Trim() : "";

    private static bool TryInt(string value, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    // No private fund has reported anywhere near a trillion dollars (the largest are a few hundred
    // billion); the archive's values at or above it are keying errors, such as a feeder fund
    // filed at $757 trillion.
    internal const long MaxPlausibleFundGrossAssets = 1_000_000_000_000;

    // A blank, unreadable or implausible gross asset value counts the fund without assets.
    internal static long ParseAssets(string value)
    {
        if (
            !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
            || v < 0
            || v >= MaxPlausibleFundGrossAssets
        )
            return 0;
        return (long)Math.Round(v, MidpointRounding.AwayFromZero);
    }

    // Registered filings carry a time ("11/13/2012 01:39:54 PM"), exempt ones only the date.
    private static DateOnly? ParseSubmitted(string value)
    {
        var date = value.Split(' ', 2)[0];
        return DateOnly.TryParseExact(
            date,
            ["MM/dd/yyyy", "M/d/yyyy"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed
        )
            ? parsed
            : null;
    }
}
