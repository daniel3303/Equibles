using Equibles.Integrations.Finra.Models;

namespace Equibles.Integrations.Finra.Contracts;

public interface IFinraClient {
    bool IsConfigured { get; }
    Task<List<ShortVolumeRecord>> GetDailyShortVolume(DateOnly date);
    Task<List<ShortInterestRecord>> GetShortInterest(DateOnly settlementDate);
    Task<List<DateOnly>> GetShortInterestSettlementDates();
}
