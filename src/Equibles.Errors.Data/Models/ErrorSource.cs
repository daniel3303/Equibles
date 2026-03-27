namespace Equibles.Errors.Data.Models;

public class ErrorSource {
    public string Value { get; }

    public ErrorSource(string value) => Value = value;

    public static readonly ErrorSource McpTool = new("McpTool");
    public static readonly ErrorSource DocumentScraper = new("DocumentScraper");
    public static readonly ErrorSource HoldingsScraper = new("HoldingsScraper");
    public static readonly ErrorSource ShortDataScraper = new("ShortDataScraper");
    public static readonly ErrorSource DocumentProcessor = new("DocumentProcessor");
    public static readonly ErrorSource CongressScraper = new("CongressScraper");
    public static readonly ErrorSource FredScraper = new("FredScraper");
    public static readonly ErrorSource Other = new("Other");

    public static IEnumerable<ErrorSource> GetAll() => [McpTool, DocumentScraper, HoldingsScraper, ShortDataScraper, DocumentProcessor, CongressScraper, FredScraper, Other];

    public override string ToString() => Value;
    public override bool Equals(object obj) => obj is ErrorSource other && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
