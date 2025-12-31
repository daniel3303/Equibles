namespace Equibles.Sec.HostedService.Services;

public interface ICompanySyncService {
    Task SyncCompaniesFromSecApi();
}
