namespace Equibles.Web.ViewModels.ShortVolume;

public class ShortVolumeListItemViewModel
{
    public string Ticker { get; set; }
    public string Name { get; set; }
    public decimal ShortVolume { get; set; }
    public decimal ShortExemptVolume { get; set; }
    public decimal TotalVolume { get; set; }
    public double ShortPercent { get; set; }
}
