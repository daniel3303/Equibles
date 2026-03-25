using System.Security.Cryptography;
using System.Text;
using Equibles.Mcp.Contracts;

namespace Equibles.Mcp.Server;

public class SimpleApiKeyValidator : IApiKeyValidator {
    private readonly byte[] _configuredKeyHash;

    public bool IsEnabled { get; }

    public SimpleApiKeyValidator(IConfiguration configuration) {
        var key = configuration["McpApiKey"] ?? "";
        IsEnabled = !string.IsNullOrEmpty(key);
        _configuredKeyHash = IsEnabled
            ? SHA256.HashData(Encoding.UTF8.GetBytes(key))
            : [];
    }

    public Task<bool> IsValid(string apiKey) {
        if (!IsEnabled) {
            return Task.FromResult(true);
        }

        var apiKeyHash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey ?? ""));
        var isValid = CryptographicOperations.FixedTimeEquals(apiKeyHash, _configuredKeyHash);
        return Task.FromResult(isValid);
    }
}
