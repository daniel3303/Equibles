namespace Equibles.Web.ViewModels.ShortActivity;

public class MostShortedListItemViewModel
{
    public string Ticker { get; set; }
    public string Name { get; set; }
    public long CurrentShortPosition { get; set; }
    public long ChangeInShortPosition { get; set; }
    public long? AverageDailyVolume { get; set; }
    public decimal? DaysToCover { get; set; }
}
