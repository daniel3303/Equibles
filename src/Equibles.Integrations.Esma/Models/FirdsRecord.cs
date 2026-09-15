namespace Equibles.Integrations.Esma.Models;

public enum FirdsRecordKind
{
    Full,
    New,
    Modified,
    Terminated,
}

// One reference-data record exactly as the file states it; the reader never infers a field.
public sealed class FirdsRecord
{
    public FirdsRecordKind Kind { get; set; }
    public string Isin { get; set; }
    public string FullName { get; set; }
    public string ShortName { get; set; }
    public string Cfi { get; set; }
    public string Currency { get; set; }
    public string Lei { get; set; }
    public string Mic { get; set; }
    public DateTime? FirstTradeDate { get; set; }
    public DateTime? TerminationDate { get; set; }
    public string RelevantCompetentAuthority { get; set; }
    public string RelevantTradingVenue { get; set; }
}
