namespace Equibles.Holdings.HostedService.Services.ValueNormalizers;

public class PassthroughValueNormalizer : IValueNormalizer {
    public static readonly PassthroughValueNormalizer Instance = new();

    public long Normalize(long rawValue) => rawValue;
}
