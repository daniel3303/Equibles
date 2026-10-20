using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Equibles.Sec.Data.Models;

/// <summary>
/// One Form ADV Item 5.D client type of a <see cref="FormAdvAdviserVersion"/>: client count and
/// regulatory assets under management. It changes only with its version, so the history keeps a
/// client mix once per stretch of identical rosters. Types reporting neither clients nor assets
/// are not stored.
/// </summary>
[Index(nameof(FormAdvAdviserVersionId), nameof(ClientType), IsUnique = true)]
public class FormAdvClientTypeAum
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid FormAdvAdviserVersionId { get; set; }
    public virtual FormAdvAdviserVersion FormAdvAdviserVersion { get; set; }

    public FormAdvClientType ClientType { get; set; }

    /// <summary>Number of clients (Item 5.D.(1)); null when not reported, or reported as 0 beside "fewer than 5".</summary>
    public int? ClientCount { get; set; }

    /// <summary>The adviser checked "fewer than 5 clients" (Item 5.D.(2)) instead of a count.</summary>
    public bool FewerThanFiveClients { get; set; }

    /// <summary>Regulatory assets under management for this client type (Item 5.D.(3)), in US dollars.</summary>
    public long? RegulatoryAum { get; set; }
}
