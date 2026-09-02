using System.ComponentModel.DataAnnotations;

namespace Equibles.Sec.Data.Models;

public enum SecFilingArtifactCaptureStatus
{
    [Display(Name = "Metadata only")]
    MetadataOnly,

    [Display(Name = "Text captured")]
    TextCaptured,

    [Display(Name = "Binary not parsed")]
    BinaryNotParsed,
}
