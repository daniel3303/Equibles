using System.Xml.Linq;
using Equibles.Core.AutoWiring;
using Equibles.InsiderTrading.Data.Models;
using Equibles.InsiderTrading.Repositories;
using Equibles.Integrations.Sec.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Equibles.InsiderTrading.BusinessLogic;

/// <summary>
/// Fills <see cref="Form144Filing.FilerCik"/> and <see cref="Form144Filing.PlanAdoptionDate"/>
/// on notices imported before those fields were captured, by re-reading each notice's XML from
/// EDGAR.
///
/// The filer CIK is what makes a notice joinable to the filer's Forms 3/4/5, and therefore the
/// only way to tell whether a proposed sale was ever executed. Without it the seller is free
/// text, and matching on a name resolves only about half of the corpus.
///
/// Never-attempted notices run before retries, and failed fetches wait between persisted attempts,
/// so a temporary EDGAR outage cannot consume the retry budget or starve later notices. Notices
/// EDGAR will not serve are parked with <see cref="UnavailableMarker"/> after
/// <see cref="MaxAttempts"/> tries.
/// </summary>
[Service]
public class Form144FilerCikBackfillManager
{
    /// <summary>
    /// Parked value for a notice whose XML EDGAR will not serve, or which carries no filer
    /// credentials. It is not a CIK and cannot collide with one, so it drops the notice out of
    /// the work set without ever matching an <see cref="InsiderOwner"/>.
    /// </summary>
    public const string UnavailableMarker = "unavailable";

    // Committed per batch, so a throttled or interrupted run keeps what it fetched.
    private const int BatchSize = 64;
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromHours(6);

    // One full batch parked for missing credentials is already past coincidence.
    private const int ParkedWithoutCredentialsAlarm = BatchSize;

    private readonly Form144FilingRepository _repository;
    private readonly ISecEdgarClient _secEdgarClient;
    private readonly ILogger<Form144FilerCikBackfillManager> _logger;
    private readonly TimeProvider _timeProvider;

    public Form144FilerCikBackfillManager(
        Form144FilingRepository repository,
        ISecEdgarClient secEdgarClient,
        ILogger<Form144FilerCikBackfillManager> logger,
        TimeProvider timeProvider
    )
    {
        _repository = repository;
        _secEdgarClient = secEdgarClient;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Drains notices missing a filer CIK. Returns the number of notices resolved to a real CIK.
    /// </summary>
    public async Task<int> Run(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolved = 0;
        var parkedNoIssuerCik = 0;
        var parkedNoCredentials = 0;
        var parkedAttemptsExhausted = 0;
        var retryBefore = (_timeProvider.GetUtcNow() - RetryDelay).UtcDateTime;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = await _repository
                .GetAll()
                .Where(f =>
                    f.FilerCik == null
                    && (
                        f.FilerCikBackfillAttemptedAt == null
                        || f.FilerCikBackfillAttemptedAt <= retryBefore
                    )
                )
                .Include(f => f.CommonStock)
                .OrderBy(f => f.FilerCikBackfillAttemptedAt != null)
                .ThenBy(f => f.FilerCikBackfillAttemptedAt)
                .ThenBy(f => f.FilingDate)
                .ThenBy(f => f.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
                break;

            foreach (var filing in batch)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    await _repository.SaveChanges();
                    _repository.ClearChangeTracker();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                var issuerCik = filing.CommonStock?.Cik;
                if (string.IsNullOrEmpty(issuerCik))
                {
                    // The notice is attributed to an issuer with no CIK, so its document cannot
                    // be addressed on EDGAR at all. Park it rather than retrying forever.
                    filing.FilerCik = UnavailableMarker;
                    parkedNoIssuerCik++;
                    continue;
                }

                if (filing.FilerCikBackfillAttempts >= MaxAttempts)
                {
                    ParkIfAttemptsExhausted(filing, ref parkedAttemptsExhausted);
                    continue;
                }

                filing.FilerCikBackfillAttempts++;
                filing.FilerCikBackfillAttemptedAt = _timeProvider.GetUtcNow().UtcDateTime;

                string xml;
                try
                {
                    xml = await _secEdgarClient.GetDocumentContent(
                        filing.AccessionNumber,
                        issuerCik
                    );
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    await _repository.SaveChanges();
                    _repository.ClearChangeTracker();
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Failed to fetch Form 144 {AccessionNumber}",
                        filing.AccessionNumber
                    );
                    ParkIfAttemptsExhausted(filing, ref parkedAttemptsExhausted);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(xml))
                {
                    ParkIfAttemptsExhausted(filing, ref parkedAttemptsExhausted);
                    continue;
                }

                var parsed = ParseIdentity(xml);
                if (parsed.FilerCik == null)
                {
                    // Fetched cleanly but carries no filer credentials. Retrying cannot change
                    // that, so park it immediately rather than burning the attempt budget.
                    filing.FilerCik = UnavailableMarker;
                    parkedNoCredentials++;
                    continue;
                }

                filing.FilerCik = parsed.FilerCik;
                filing.PlanAdoptionDate = parsed.PlanAdoptionDate;
                resolved++;
            }

            await _repository.SaveChanges();
            _repository.ClearChangeTracker();
        }

        var parked = parkedNoIssuerCik + parkedNoCredentials + parkedAttemptsExhausted;
        if (parked > 0)
        {
            _logger.LogInformation(
                "Form 144 filer-CIK backfill parked {Parked} notice(s): {NoIssuerCik} with no "
                    + "issuer CIK, {NoCredentials} carrying no filer credentials, {Exhausted} after "
                    + "{MaxAttempts} failed fetches. Resolved {Resolved}.",
                parked,
                parkedNoIssuerCik,
                parkedNoCredentials,
                parkedAttemptsExhausted,
                MaxAttempts,
                resolved
            );
        }

        // Notices EDGAR serves but that carry no filer credentials are the exception, not the
        // rule: every electronically filed Form 144 has them. A cycle that parks far more than
        // it resolves is a fault in THIS lane (a document shape it cannot read), not a corpus
        // of bad filings, and parking is silent enough to burn the whole backlog unnoticed.
        if (parkedNoCredentials > resolved && parkedNoCredentials >= ParkedWithoutCredentialsAlarm)
        {
            _logger.LogWarning(
                "Form 144 filer-CIK backfill parked {NoCredentials} notice(s) for missing filer "
                    + "credentials while resolving only {Resolved}. Expect nearly every notice to "
                    + "carry credentials, so this points at the document shape this lane reads, not "
                    + "at the filings. Investigate before the backlog is exhausted.",
                parkedNoCredentials,
                resolved
            );
        }

        return resolved;
    }

    private void ParkIfAttemptsExhausted(Form144Filing filing, ref int parkedAttemptsExhausted)
    {
        if (filing.FilerCikBackfillAttempts < MaxAttempts)
            return;

        filing.FilerCik = UnavailableMarker;
        parkedAttemptsExhausted++;
        _logger.LogWarning(
            "Parking Form 144 {AccessionNumber} after {Attempts} failed attempts",
            filing.AccessionNumber,
            MaxAttempts
        );
    }

    /// <summary>
    /// Reads the filer CIK and any Rule 10b5-1 plan adoption date out of a notice's XML.
    /// Kept here rather than on the processor because the backfill runs from a raw document
    /// with no surrounding filing metadata.
    /// </summary>
    /// <summary>
    /// Reads the filer CIK and earliest plan adoption date out of a notice's RAW EDGAR
    /// submission.
    ///
    /// The document arrives as the full <c>.txt</c> submission, which opens with an SGML
    /// envelope (<c>SEC-DOCUMENT</c>, <c>SEC-HEADER</c>) and is NOT well-formed XML, so it must
    /// be sanitized exactly as the import path sanitizes it before it can be parsed. Parsing the
    /// raw text instead throws on every notice, which reads here as "carries no filer
    /// credentials" and quietly parks the entire corpus.
    /// </summary>
    internal static (string FilerCik, DateOnly? PlanAdoptionDate) ParseIdentity(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return (null, null);

        XElement root;
        try
        {
            root = XDocument.Parse(InsiderFilingParser.SanitizeXml(xml)).Root;
        }
        catch (System.Xml.XmlException)
        {
            return (null, null);
        }

        if (root == null)
            return (null, null);

        var filer = Child(Child(Child(root, "headerData"), "filerInfo"), "filer");
        var cik = Value(Child(filer, "filerCredentials"), "cik");

        var adoptionDates = Children(
                Child(Child(Child(root, "formData"), "noticeSignature"), "planAdoptionDates"),
                "planAdoptionDate"
            )
            .Select(e => ParseUsDate(e.Value))
            .Where(d => d != null)
            .ToList();

        return (
            string.IsNullOrEmpty(cik) ? null : cik,
            adoptionDates.Count == 0 ? null : adoptionDates.Min()
        );
    }

    // Namespace-agnostic navigation: Form 144 documents carry the edgar/ownership namespace on
    // some elements and a common namespace on others.
    private static XElement Child(XElement parent, string name) =>
        parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name);

    private static IEnumerable<XElement> Children(XElement parent, string name) =>
        parent?.Elements().Where(e => e.Name.LocalName == name) ?? [];

    private static string Value(XElement parent, string name) => Child(parent, name)?.Value?.Trim();

    private static DateOnly? ParseUsDate(string value)
    {
        return DateOnly.TryParseExact(
            value?.Trim(),
            ["MM/dd/yyyy", "M/d/yyyy"],
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out var parsed
        )
            ? parsed
            : null;
    }
}
