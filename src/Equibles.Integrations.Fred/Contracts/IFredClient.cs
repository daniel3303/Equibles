using Equibles.Integrations.Fred.Models;

namespace Equibles.Integrations.Fred.Contracts;

public interface IFredClient {
    bool IsConfigured { get; }
    Task<FredSeriesRecord> GetSeriesMetadata(string seriesId);
    Task<List<FredObservationRecord>> GetObservations(string seriesId, DateOnly? startDate = null);
}
