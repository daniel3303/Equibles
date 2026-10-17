using System.Text;
using System.Xml;
using Equibles.Core.AutoWiring;
using Equibles.Core.Extensions;
using Equibles.Holdings.HostedService.Models;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Equibles.Integrations.Sec.Contracts;
using Microsoft.Extensions.Logging;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// One-off correction: holders created before identity numbers refreshed from each new cover page
/// still carry the numbers of the first cover ever seen (often blank, since CRD and SEC file
/// numbers joined the form in 2023, or a parent's numbers the old cover parser borrowed). Reads
/// each such holder's newest 13F cover page once and stamps <c>IdentityAccession</c>; imports keep
/// it current afterwards.
/// </summary>
[Service]
public class HolderIdentityBackfillService(
    ISecEdgarClient edgarClient,
    Filing13FXmlParser parser,
    InstitutionalHolderRepository holderRepository,
    ILogger<HolderIdentityBackfillService> logger
)
{
    private const string CoverPageFileName = "primary_doc.xml";

    // A NEW HOLDINGS amendment's cover is skipped for the filing before it; three covers reach
    // an original or RESTATEMENT for any realistic amendment run.
    internal const int FilingsPerHolder = 3;

    /// <summary>
    /// Reads up to <paramref name="maxHolders"/> holders' cover pages and returns the holders
    /// stamped. Stops at the first transient SEC failure, so an outage costs one request.
    /// </summary>
    public async Task<int> Run(int maxHolders, CancellationToken cancellationToken)
    {
        var candidates = await holderRepository.GetIdentityBackfillCandidates(
            maxHolders,
            FilingsPerHolder,
            cancellationToken
        );
        var stamped = 0;
        var visited = 0;
        foreach (var candidate in candidates)
        {
            CoverRead read;
            try
            {
                read = await ReadNewestReportCover(candidate, cancellationToken);
            }
            catch (Exception exception)
                when (exception is HttpRequestException or TaskCanceledException
                    && !cancellationToken.IsCancellationRequested
                )
            {
                logger.LogInformation(
                    exception,
                    "SEC unavailable during the holder identity backfill; resuming next cycle"
                );
                break;
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(
                    exception,
                    "Holder identity backfill failed for holder {HolderId}; skipping it this cycle",
                    candidate.HolderId
                );
                visited++;
                continue;
            }

            visited++;
            stamped += await holderRepository.ApplyBackfilledIdentity(
                candidate.HolderId,
                read.AccessionNumber,
                Resolve(
                    read,
                    candidate.Form13FFileNumber,
                    m => m.Form13FFileNumber,
                    f => f.Form13FFileNumber
                ),
                Resolve(read, candidate.CrdNumber, m => m.CrdNumber, f => f.CrdNumber),
                Resolve(read, candidate.SecFileNumber, m => m.SecFileNumber, f => f.SecFileNumber),
                cancellationToken
            );
        }

        if (candidates.Count > 0)
            logger.LogInformation(
                "Holder identity backfill stamped {Stamped} of {Visited} visited holders ({Candidates} candidates)",
                stamped,
                visited,
                candidates.Count
            );
        return stamped;
    }

    private static string Resolve(
        CoverRead read,
        string storedValue,
        Func<OtherManagerIdentity, string> otherManagerValue,
        Func<Parsed13FFiling, string> coverValue
    ) =>
        HolderIdentityNumbers
            .Resolve(
                read.Filing == null ? null : coverValue(read.Filing),
                storedValue,
                read.Filing?.CoverPageOtherManagers.Select(otherManagerValue) ?? []
            )
            .TruncateToFit(32);

    // The newest filing whose cover speaks for the filer: a NEW HOLDINGS amendment only adds
    // positions, so the filing before it is read instead while one remains. An unpublished or
    // unparseable cover is final and stamps the holder with its stored numbers normalized.
    private async Task<CoverRead> ReadNewestReportCover(
        HolderIdentityBackfillCandidate candidate,
        CancellationToken cancellationToken
    )
    {
        var newest = candidate.Filings[0];
        if (string.IsNullOrWhiteSpace(candidate.Cik))
            return new CoverRead(newest.AccessionNumber, null);

        for (var index = 0; index < candidate.Filings.Count; index++)
        {
            var filing = candidate.Filings[index];
            var bytes = await edgarClient.GetDocumentFileBytes(
                candidate.Cik,
                filing.AccessionNumber,
                CoverPageFileName,
                cancellationToken
            );
            if (bytes.Length == 0)
                return new CoverRead(filing.AccessionNumber, null);

            Parsed13FFiling parsed;
            try
            {
                parsed = parser.ParseCoverPage(
                    Encoding.UTF8.GetString(bytes),
                    filing.AccessionNumber,
                    candidate.Cik,
                    filing.FilingDate
                );
            }
            catch (Exception exception) when (exception is XmlException or FormatException)
            {
                logger.LogWarning(
                    exception,
                    "Cover page of {Accession} does not parse; keeping the stored identity numbers",
                    filing.AccessionNumber
                );
                return new CoverRead(filing.AccessionNumber, null);
            }

            var isNewHoldings = string.Equals(
                parsed.AmendmentType,
                "NEW HOLDINGS",
                StringComparison.OrdinalIgnoreCase
            );
            if (!isNewHoldings || index == candidate.Filings.Count - 1)
                return new CoverRead(filing.AccessionNumber, parsed);
        }

        return new CoverRead(newest.AccessionNumber, null);
    }

    private sealed record CoverRead(string AccessionNumber, Parsed13FFiling Filing);
}
