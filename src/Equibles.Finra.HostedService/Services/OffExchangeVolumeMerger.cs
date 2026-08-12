using Equibles.Finra.Data.Models;
using Equibles.Integrations.Finra.Models;

namespace Equibles.Finra.HostedService.Services;

internal static class OffExchangeVolumeMerger
{
    private const string AtsSummaryTypeCode = "ATS_W_SMBL";
    private const string NonAtsOtcSummaryTypeCode = "OTC_W_SMBL";

    public static Dictionary<Guid, OffExchangeVolume> Merge(
        IEnumerable<OffExchangeWeeklyRecord> records,
        IReadOnlyDictionary<string, Guid> tickerMap,
        IReadOnlyDictionary<string, Guid> compressedIndex,
        DateOnly weekStartDate
    )
    {
        var merged = new Dictionary<Guid, OffExchangeVolume>();
        foreach (var record in records)
        {
            // The weekly feed spells class shares with a dot ("BRK.B"); resolution bridges
            // FINRA's spellings onto the stored dash tickers so class-share weeks stop
            // dropping silently (#4369). Casing stays Ordinal — a lowercase suffix is a
            // different security.
            if (
                !FinraClassShareSymbols.TryResolve(
                    tickerMap,
                    compressedIndex,
                    record.Symbol,
                    out var commonStockId
                )
            )
            {
                continue;
            }

            if (!merged.TryGetValue(commonStockId, out var volume))
            {
                volume = new OffExchangeVolume
                {
                    CommonStockId = commonStockId,
                    WeekStartDate = weekStartDate,
                };
                merged[commonStockId] = volume;
            }

            AddRecord(volume, record);
        }

        return merged;
    }

    private static void AddRecord(OffExchangeVolume volume, OffExchangeWeeklyRecord record)
    {
        var shares = record.TotalWeeklyShareQuantity ?? 0;
        var trades = record.TotalWeeklyTradeCount ?? 0;
        if (record.SummaryTypeCode == AtsSummaryTypeCode)
        {
            volume.AtsVolume += shares;
            volume.AtsTradeCount += trades;
        }
        else if (record.SummaryTypeCode == NonAtsOtcSummaryTypeCode)
        {
            volume.NonAtsOtcVolume += shares;
            volume.NonAtsOtcTradeCount += trades;
        }
    }
}
