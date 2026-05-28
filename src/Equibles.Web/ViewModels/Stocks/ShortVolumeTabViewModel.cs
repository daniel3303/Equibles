using Equibles.Finra.Data.Models;

namespace Equibles.Web.ViewModels.Stocks;

public class ShortVolumeTabViewModel : StockTabViewModel
{
    public List<DailyShortVolume> ShortVolumes { get; set; } = [];
}
