namespace Equibles.Holdings.HostedService.Services.ValueNormalizers;

public interface IValueNormalizer {
    long Normalize(long rawValue);
}
