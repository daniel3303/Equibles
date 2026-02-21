using Equibles.ShortData.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class ShortVolumeTabViewModel {
    public List<DailyShortVolume> ShortVolumes { get; set; } = [];
    public string Ticker { get; set; }
}
