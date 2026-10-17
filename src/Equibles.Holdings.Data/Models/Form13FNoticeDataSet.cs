using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Holdings.Data.Models;

/// <summary>A 13F data set whose notices have been read, so the notice sync does not fetch it again.</summary>
[Index(nameof(FileName), IsUnique = true)]
public class Form13FNoticeDataSet
{
    /// <summary>Bump to read every data set's notices again after a reader change.</summary>
    public const int CurrentParserVersion = 1;

    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(128)]
    public string FileName { get; set; }

    public int NoticeCount { get; set; }
    public int ParserVersion { get; set; }
    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}
