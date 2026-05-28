using System.Numerics;

namespace Equibles.Web.Extensions;

public static class SignedFormatting
{
    public static string ToStringWithSign<T>(this T value, string format)
        where T : INumber<T> => (value > T.Zero ? "+" : "") + value.ToString(format, null);
}
