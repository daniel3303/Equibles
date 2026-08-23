namespace Equibles.Sec.HostedService.Models;

public class DocumentNormalizationBackfillResult
{
    public int Processed { get; set; }

    public int Replaced { get; set; }

    public int Unchanged { get; set; }

    public int Failed { get; set; }
}
