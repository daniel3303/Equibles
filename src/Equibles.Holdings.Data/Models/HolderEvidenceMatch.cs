using System.ComponentModel.DataAnnotations;

namespace Equibles.Holdings.Data.Models;

/// <summary>Which filed identifier tied a record to the holder, strongest first.</summary>
public enum HolderEvidenceMatch
{
    [Display(Name = "CRD on the holder's 13F cover page")]
    CoverCrd = 0,

    [Display(Name = "SEC file number on the holder's 13F cover page")]
    CoverSecFileNumber = 1,

    [Display(Name = "CIK on the record")]
    Cik = 2,
}
