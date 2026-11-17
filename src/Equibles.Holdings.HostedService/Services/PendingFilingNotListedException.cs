namespace Equibles.Holdings.HostedService.Services;

/// <summary>
/// The filer's submissions list does not carry a pending filing EDGAR already publishes: the list
/// lags acceptance by minutes, so the filing is retried soon instead of after the usual cooldown.
/// </summary>
public class PendingFilingNotListedException(string message) : Exception(message);
