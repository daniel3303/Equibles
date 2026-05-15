namespace Equibles.Congress.HostedService.Services;

/// <summary>
/// Thrown by <see cref="ISenateBrowserSession"/> when the underlying browser
/// transport fails (timeout, navigation error, evaluation failure). Lets
/// <see cref="SenateDisclosureClient"/> own the retry/backoff policy without a
/// hard dependency on the Playwright exception types.
/// </summary>
public class SenateBrowserException : Exception
{
    public SenateBrowserException(string message, Exception innerException)
        : base(message, innerException) { }
}
