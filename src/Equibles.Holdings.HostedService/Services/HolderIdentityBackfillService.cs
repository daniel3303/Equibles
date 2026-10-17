using System.Text;
using System.Xml;
using Equibles.Core.AutoWiring;
using Equibles.Core.Extensions;
using Equibles.Holdings.Repositories;
using Equibles.Holdings.Repositories.Models;
using Equibles.Integrations.Sec.Contracts;
using Microsoft.Extensions.Logging;

namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// One-off correction: holders created before identity numbers refreshed from each new cover page
/// still carry the numbers of the first cover ever seen (often blank, since CRD and SEC file
/// numbers joined the form in 2023). Reads each such holder's newest 13F cover page once and
/// stamps <c>IdentityAccession</c>; imports keep it current afterwards.
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

    /// <summary>Reads up to <paramref name="maxHolders"/> cover pages; returns the holders stamped.</summary>
    public async Task<int> Run(int maxHolders, CancellationToken cancellationToken)
    {
        var candidates = await holderRepository.GetIdentityBackfillCandidates(
            maxHolders,
            cancellationToken
        );
        var stamped = 0;
        var transient = 0;
        foreach (var candidate in candidates)
        {
            var identity = await ReadCoverIdentity(candidate, cancellationToken);
            if (identity == null)
            {
                transient++;
                continue;
            }
            stamped += await holderRepository.ApplyBackfilledIdentity(
                candidate.HolderId,
                candidate.AccessionNumber,
                identity.Form13FFileNumber.TruncateToFit(32),
                identity.CrdNumber.TruncateToFit(32),
                identity.SecFileNumber.TruncateToFit(32),
                cancellationToken
            );
        }

        if (candidates.Count > 0)
            logger.LogInformation(
                "Holder identity backfill stamped {Stamped}/{Candidates} holders ({Transient} left for a later cycle)",
                stamped,
                candidates.Count,
                transient
            );
        return stamped;
    }

    // Null means a transient failure to retry later; an unpublished or unparseable cover page is
    // final and yields an empty identity, so the holder is stamped and never fetched again.
    private async Task<CoverIdentity> ReadCoverIdentity(
        HolderIdentityBackfillCandidate candidate,
        CancellationToken cancellationToken
    )
    {
        byte[] bytes;
        try
        {
            bytes = await edgarClient.GetDocumentFileBytes(
                candidate.Cik,
                candidate.AccessionNumber,
                CoverPageFileName,
                cancellationToken
            );
        }
        catch (HttpRequestException exception)
        {
            logger.LogInformation(
                exception,
                "Cover page of {Accession} unavailable; retrying on a later cycle",
                candidate.AccessionNumber
            );
            return null;
        }

        if (bytes.Length == 0)
            return new CoverIdentity(null, null, null);

        try
        {
            var filing = parser.ParseCoverPage(
                Encoding.UTF8.GetString(bytes),
                candidate.AccessionNumber,
                candidate.Cik,
                candidate.FilingDate
            );
            return new CoverIdentity(
                filing.Form13FFileNumber,
                filing.CrdNumber,
                filing.SecFileNumber
            );
        }
        catch (Exception exception) when (exception is XmlException or FormatException)
        {
            logger.LogWarning(
                exception,
                "Cover page of {Accession} does not parse; stamping without identity numbers",
                candidate.AccessionNumber
            );
            return new CoverIdentity(null, null, null);
        }
    }

    private sealed record CoverIdentity(
        string Form13FFileNumber,
        string CrdNumber,
        string SecFileNumber
    );
}
