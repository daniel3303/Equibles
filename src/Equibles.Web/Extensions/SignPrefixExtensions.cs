using System.Numerics;

namespace Equibles.Web.Extensions;

public static class SignPrefixExtensions
{
    public static string SignPrefix<T>(this T value)
        where T : INumber<T> => value > T.Zero ? "+" : string.Empty;

    public static string SignPrefix<T>(this T? value)
        where T : struct, INumber<T> => value.HasValue ? value.Value.SignPrefix() : string.Empty;
}
