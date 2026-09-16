namespace Equibles.Integrations.Lse.Models;

// One published edition of the instrument list: the workbook's own as-at date and stated count travel with it.
public sealed class LseInstrumentList
{
    public Uri PageUrl { get; set; }
    public Uri SourceUrl { get; set; }
    public int Edition { get; set; }
    public DateOnly AsAt { get; set; }
    public int StatedCount { get; set; }
    public DateTime CapturedAt { get; set; }
    public List<LseInstrument> Instruments { get; set; } = [];
}
