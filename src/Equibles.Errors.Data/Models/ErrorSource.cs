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
    public static readonly ErrorSource TranscriptScraper = new("TranscriptScraper");
    public static readonly ErrorSource InsiderTradingReprocess = new("InsiderTradingReprocess");
    public static readonly ErrorSource NportReprocess = new("NportReprocess");
    public static readonly ErrorSource WebsiteDiscovery = new("WebsiteDiscovery");
    public static readonly ErrorSource InvestorRelationsDiscovery = new(
        "InvestorRelationsDiscovery"
    );
    public static readonly ErrorSource InvestorRelationsScraper = new("InvestorRelationsScraper");
    public static readonly ErrorSource Other = new("Other");

    public static IEnumerable<ErrorSource> GetAll() =>
        [
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
            TranscriptScraper,
            InsiderTradingReprocess,
            NportReprocess,
            WebsiteDiscovery,
            InvestorRelationsDiscovery,
            InvestorRelationsScraper,
            Other,
        ];

    public override string ToString() => Value;

    public override bool Equals(object obj) => obj is ErrorSource other && Value == other.Value;

    public override int GetHashCode() => Value.GetHashCode();
}
