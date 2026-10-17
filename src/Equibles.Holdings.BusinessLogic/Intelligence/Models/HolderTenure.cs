namespace Equibles.Holdings.BusinessLogic.Intelligence.Models;

/// <summary>
/// How many consecutive quarters a filer has held a security. Censored means the run reaches the
/// first covered quarter, so the true tenure is at least <see cref="Quarters"/>.
/// </summary>
public readonly record struct HolderTenure(int Quarters, bool Censored);
