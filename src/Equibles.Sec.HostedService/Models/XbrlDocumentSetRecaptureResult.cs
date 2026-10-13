namespace Equibles.Sec.HostedService.Models;

public class XbrlDocumentSetRecaptureResult
{
    public int Processed { get; set; }
    public int Grown { get; set; }
    public int Unchanged { get; set; }
    public int Failed { get; set; }
}
