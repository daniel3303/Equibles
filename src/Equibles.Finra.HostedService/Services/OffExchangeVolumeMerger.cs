using Equibles.CommonStocks.Data.Models;
using Equibles.Finra.Data.Models;
using Equibles.Integrations.Finra.Models;

namespace Equibles.Finra.HostedService.Services;

internal static class OffExchangeVolumeMerger
{
    private const string AtsSummaryTypeCode = "ATS_W_SMBL";
    private const string NonAtsOtcSummaryTypeCode = "OTC_W_SMBL";

    public static Dictionary<ListedSecurityKey, OffExchangeVolume> Merge(
        IEnumerable<OffExchangeWeeklyRecord> records,
        IReadOnlyDictionary<string, ListedSecurityKey> tickerMap,
        IReadOnlyDictionary<string, ListedSecurityKey> compressedIndex,
        DateOnly weekStartDate
    )
    {
        var merged = new Dictionary<ListedSecurityKey, OffExchangeVolume>();
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
                    out var listing
                )
            )
            {
                continue;
            }

            if (!merged.TryGetValue(listing, out var volume))
            {
                volume = new OffExchangeVolume
                {
                    CommonStockId = listing.CommonStockId,
                    ListedTicker = listing.ListedTicker,
                    WeekStartDate = weekStartDate,
                };
                merged[listing] = volume;
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
