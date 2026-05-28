namespace Equibles.Worker;

public interface IImporter
{
    Task Import(CancellationToken cancellationToken);
}
