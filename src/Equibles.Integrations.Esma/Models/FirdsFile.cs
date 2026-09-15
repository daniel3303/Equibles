namespace Equibles.Integrations.Esma.Models;

public enum FirdsFileType
{
    Full,
    Delta,
}

// A published file as the authority's index lists it; the checksum is absent where the index has none.
public sealed class FirdsFile
{
    public string Authority { get; set; }
    public string FileName { get; set; }
    public FirdsFileType FileType { get; set; }
    public DateOnly PublishedOn { get; set; }
    public Uri DownloadUrl { get; set; }
    public string Checksum { get; set; }
}
