namespace Equibles.Core.Configuration;

public class WorkerOptions {
    public DateTime? MinSyncDate { get; set; }
    public List<string> TickersToSync { get; set; } = [];
}
