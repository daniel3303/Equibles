using System.Xml.Linq;
using Equibles.CommonStocks.Data.Models;
using Equibles.Errors.BusinessLogic;
using Equibles.Integrations.Sec.Contracts;
using Equibles.Integrations.Sec.Models;
using Equibles.Sec.Data.Models;
using Equibles.Sec.HostedService.Contracts;
using Equibles.Sec.HostedService.Helpers;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.HostedService.Services;

/// <summary>
/// Template-method base for filing processors that turn a single issuer-feed XML submission into one
/// structured filing entity (Form 144, Form D, NPORT-P, N-CEN). The fixed pipeline — open a scope,
/// skip an already-imported accession number, fetch and parse the submission, then persist — lives
/// here; each form supplies only its labels, its accession lookup, its parse, and its success log line.
/// </summary>
public abstract class IssuerFeedFilingProcessor<TEntity, TRepository> : IFilingProcessor
    where TEntity : class
    where TRepository : BaseRepository<TEntity>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ErrorReporter _errorReporter;

    // Carries the concrete processor's log category, so messages emitted from this base keep the
    // same SourceContext they had when the pipeline lived in each processor.
    protected ILogger Logger { get; }

    protected IssuerFeedFilingProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        ErrorReporter errorReporter
    )
    {
        _scopeFactory = scopeFactory;
        Logger = logger;
        _errorReporter = errorReporter;
    }

    public abstract bool CanProcess(DocumentType documentType);

    // Human-readable form name used in log messages (e.g. "Form 144", "NPORT-P").
    protected abstract string FormLabel { get; }

    // Context key passed to the shared parser for error attribution (e.g. "Form144.ParseXml").
    protected abstract string ParseContext { get; }

    // Root element ParseFiling requires; named in the warning when the submission lacks it.
    protected abstract string RequiredSection { get; }

    protected abstract IQueryable<TEntity> GetByAccessionNumber(
        TRepository repository,
        string accessionNumber
    );

    protected abstract TEntity ParseFiling(XElement root, Guid companyId, FilingData filing);

    protected abstract void LogImported(TEntity entity, string ticker, string accessionNumber);

    public async Task<HashSet<string>> FilterKnownAccessions(
        IReadOnlyCollection<string> accessionNumbers
    )
    {
        if (accessionNumbers.Count == 0)
            return [];

        await using var scope = _scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<TRepository>();

        // Every issuer-feed entity stores the filing's accession number under a
        // unique index; EF.Property keeps the lookup generic without forcing a
        // per-form abstract member alongside GetByAccessionNumber.
        var candidates = accessionNumbers.ToList();
        var known = await repository
            .GetAll()
            .Where(e => candidates.Contains(EF.Property<string>(e, "AccessionNumber")))
            .Select(e => EF.Property<string>(e, "AccessionNumber"))
            .ToListAsync();

        return known.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> Process(FilingData filing, CommonStock companyOutContext)
    {
        // Capture IDs from the outer-scope entity to avoid leaking untracked entities into inner scope.
        var companyId = companyOutContext.Id;
        var companyTicker = companyOutContext.Ticker;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var secEdgarClient = scope.ServiceProvider.GetRequiredService<ISecEdgarClient>();
        var repository = scope.ServiceProvider.GetRequiredService<TRepository>();

        var existing = await GetByAccessionNumber(repository, filing.AccessionNumber).AnyAsync();
        if (existing)
            return false;

        var content = await secEdgarClient.GetDocumentContent(filing);
        if (string.IsNullOrWhiteSpace(content))
        {
            Logger.LogWarning(
                "Empty content for {Ticker} {Form} - {AccessionNumber}",
                companyTicker,
                FormLabel,
                filing.AccessionNumber
            );
            return false;
        }

        var root = await EdgarXmlSubmissionParser.TryParseSubmission(
            content,
            filing,
            companyTicker,
            FormLabel,
            ParseContext,
            Logger,
            _errorReporter
        );
        if (root == null)
            return false;

        var entity = ParseFiling(root, companyId, filing);
        if (entity == null)
        {
            Logger.LogWarning(
                "{Form} XML missing {Section} for {Ticker} - {AccessionNumber}",
                FormLabel,
                RequiredSection,
                companyTicker,
                filing.AccessionNumber
            );
            return false;
        }

        repository.Add(entity);
        await repository.SaveChanges();

        LogImported(entity, companyTicker, filing.AccessionNumber);

        return true;
    }
}
