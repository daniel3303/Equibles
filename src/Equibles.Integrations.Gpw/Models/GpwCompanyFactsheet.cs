namespace Equibles.Integrations.Gpw.Models;

// The company page states its name, ISIN and shortcut in its own markup; it is the row's product confirmation.
public sealed class GpwCompanyFactsheet
{
    public string Name { get; set; }
    public string Isin { get; set; }
    public string Shortcut { get; set; }
    public Uri SourceUrl { get; set; }
}
