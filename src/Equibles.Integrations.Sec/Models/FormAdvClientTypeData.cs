namespace Equibles.Integrations.Sec.Models;

/// <summary>One Form ADV Item 5.D client type row; <see cref="Index"/> is the letter's position, (a) = 0 to (n) = 13.</summary>
public class FormAdvClientTypeData
{
    public int Index { get; set; }
    public int? ClientCount { get; set; }
    public bool FewerThanFiveClients { get; set; }
    public long? RegulatoryAum { get; set; }
}
