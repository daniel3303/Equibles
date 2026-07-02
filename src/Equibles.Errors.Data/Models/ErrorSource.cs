namespace Equibles.Errors.Data.Models;

public sealed class ErrorSource
{
    public string Value { get; }

    public ErrorSource(string value) => Value = value;

    public static readonly ErrorSource McpTool = new("McpTool");
    public static readonly ErrorSource DocumentScraper = new("DocumentScraper");
    public static readonly ErrorSource HoldingsScraper = new("HoldingsScraper");
    public static readonly ErrorSource FinraScraper = new("FinraScraper");
    public static readonly ErrorSource FtdScraper = new("FtdScraper");
    public static readonly ErrorSource FormAdvScraper = new("FormAdvScraper");
    public static readonly ErrorSource FinancialFactsScraper = new("FinancialFactsScraper");
    public static readonly ErrorSource DocumentProcessor = new("DocumentProcessor");
    public static readonly ErrorSource CongressScraper = new("CongressScraper");
    public static readonly ErrorSource FredScraper = new("FredScraper");
    public static readonly ErrorSource YahooPriceScraper = new("YahooPriceScraper");
    public static readonly ErrorSource CftcScraper = new("CftcScraper");
    public static readonly ErrorSource CboeScraper = new("CboeScraper");
    public static readonly ErrorSource GovernmentContractsScraper = new(
        "GovernmentContractsScraper"
    );
    public static readonly ErrorSource TranscriptScraper = new("TranscriptScraper");
    public static readonly ErrorSource InsiderTradingReprocess = new("InsiderTradingReprocess");
    public static readonly ErrorSource NportReprocess = new("NportReprocess");
    public static readonly ErrorSource NportSweep = new("NportSweep");
    public static readonly ErrorSource WebsiteDiscovery = new("WebsiteDiscovery");
    public static readonly ErrorSource FdaCatalystScraper = new("FdaCatalystScraper");
    public static readonly ErrorSource Authentication = new("Authentication");
    public static readonly ErrorSource Alvis = new("Alvis");
    public static readonly ErrorSource WebRequest = new("WebRequest");
    public static readonly ErrorSource Other = new("Other");

    // Host-registered sources (a commercial module's own error buckets) appended by Register;
    // GetAll unions them so dashboards and filters see every source the deployment writes.
    private static readonly List<ErrorSource> Registered = [];

    public static void Register(ErrorSource source)
    {
        if (!Registered.Contains(source))
        {
            Registered.Add(source);
        }
    }

    public static IEnumerable<ErrorSource> GetAll() =>
        Registered.Concat<ErrorSource>([
            McpTool,
            DocumentScraper,
            HoldingsScraper,
            FinraScraper,
            FtdScraper,
            FormAdvScraper,
            FinancialFactsScraper,
            DocumentProcessor,
            CongressScraper,
            FredScraper,
            YahooPriceScraper,
            CftcScraper,
            CboeScraper,
            GovernmentContractsScraper,
            TranscriptScraper,
            InsiderTradingReprocess,
            NportReprocess,
            NportSweep,
            WebsiteDiscovery,
            FdaCatalystScraper,
            Authentication,
            Alvis,
            WebRequest,
            Other,
        ]);

    public override string ToString() => Value;

    public override bool Equals(object obj) => obj is ErrorSource other && Value == other.Value;

    public override int GetHashCode() => Value.GetHashCode();
}
