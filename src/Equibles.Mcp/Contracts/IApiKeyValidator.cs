namespace Equibles.Mcp.Contracts;

public interface IApiKeyValidator {
    bool IsEnabled { get; }
    Task<bool> IsValid(string apiKey);
}
