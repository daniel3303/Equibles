using Equibles.Integrations.Esma.Models;

namespace Equibles.Integrations.Esma;

// One national register's published file index plus its download; ESMA and the FCA share the file format.
public interface IFirdsFileIndex
{
    string Authority { get; }

    // Equity full files and every delta published on or after the date, oldest first.
    Task<IReadOnlyList<FirdsFile>> ListEquityFiles(
        DateOnly publishedOnOrAfter,
        CancellationToken cancellationToken = default
    );

    Task<FirdsDownload> Download(FirdsFile file, CancellationToken cancellationToken = default);
}
