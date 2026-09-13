using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace Equibles.CommonStocks.Data.Models;

[Index(nameof(Source), nameof(Identifier), IsUnique = true)]
public class EquityIssuerSourceIdentifier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EquityIssuerId { get; set; }
    public virtual EquityIssuer Issuer { get; set; }

    [Required, MaxLength(64)]
    public string Source { get; set; }

    [Required, MaxLength(128)]
    public string Identifier { get; set; }
    public Guid SourceRecordId { get; set; }
    public virtual EquityDirectorySourceRecord SourceRecord { get; set; }
}
