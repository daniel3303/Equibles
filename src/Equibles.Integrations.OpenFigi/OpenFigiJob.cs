namespace Equibles.Integrations.OpenFigi;

// One mapping job: an identifier type and value, optionally narrowed to one Bloomberg exchange code.
public sealed record OpenFigiJob(string IdType, string IdValue, string ExchangeCode)
{
    public string Key => IdType + "|" + IdValue + "|" + ExchangeCode;

    public static OpenFigiJob Parse(string key)
    {
        var parts = key.Split('|');
        return parts.Length == 3
            ? new(parts[0], parts[1], parts[2])
            : throw new ArgumentException("Not an OpenFIGI job key.", nameof(key));
    }
}
