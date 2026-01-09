namespace Equibles.Holdings.HostedService.Services.ValueNormalizers;

public class ThousandsValueNormalizer : IValueNormalizer {
    public static readonly ThousandsValueNormalizer Instance = new();

    public long Normalize(long rawValue) => checked(rawValue * 1000);
}
