using Equibles.InsiderTrading.Data.Models;

namespace Equibles.InsiderTrading.Repositories.Models;

/// <summary>
/// The closing row of each balance one filing reports: one per security held directly, and one per
/// indirect holding vehicle.
/// </summary>
/// <param name="Rows">Each balance's closing row, as filed.</param>
/// <param name="IndirectAmbiguous">
/// True when the filing's indirect rows do not show which balances belong to one vehicle: a trade
/// that continues no balance joined one only by its stated vehicle, two balances carry the same
/// stated vehicle, or rows naming no vehicle hold balances no trade connects.
/// </param>
public sealed record InsiderPositionClosings(List<InsiderTransaction> Rows, bool IndirectAmbiguous);
