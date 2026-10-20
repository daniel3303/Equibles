namespace Equibles.Holdings.BusinessLogic.Intelligence;

/// <summary>
/// A book's value per sector, the weights sector active share compares. An issuer without a
/// sector weighs under <see cref="Unclassified"/>, on the filer's side and the benchmark's alike.
/// </summary>
public static class HolderSectorWeights
{
    public static readonly Guid Unclassified = Guid.Empty;

    public static Dictionary<Guid, decimal> Of(
        IReadOnlyDictionary<Guid, decimal> issuerValues,
        IReadOnlyDictionary<Guid, Guid> issuerSectors
    )
    {
        var weights = new Dictionary<Guid, decimal>();
        foreach (var (issuerId, value) in issuerValues)
        {
            if (value <= 0)
                continue;
            var sector = issuerSectors.TryGetValue(issuerId, out var known) ? known : Unclassified;
            weights[sector] = weights.GetValueOrDefault(sector) + value;
        }
        return weights;
    }
}
