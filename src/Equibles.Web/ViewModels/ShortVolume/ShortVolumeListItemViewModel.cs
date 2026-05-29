namespace Equibles.Web.ViewModels.ShortVolume;

public class ShortVolumeListItemViewModel
{
    public string Ticker { get; set; }
    public string Name { get; set; }
    public long ShortVolume { get; set; }
    public long ShortExemptVolume { get; set; }
    public long TotalVolume { get; set; }
    public double ShortPercent { get; set; }
}
