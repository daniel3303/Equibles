namespace Equibles.Web.ViewModels.Profiles;

// One pre-selected institution chip rendered on first load of the overlap / compare /
// combined pages. Server-side rendering keeps the chip strip complete even before the
// picker JS module boots, and lets the view show "(name unknown)" for missing CIKs.
public class InstitutionPick
{
    public string Cik { get; set; }
    public string Name { get; set; }
}
