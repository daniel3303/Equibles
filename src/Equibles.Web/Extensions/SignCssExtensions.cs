using System.Numerics;

namespace Equibles.Web.Extensions;

public static class SignCssExtensions
{
    public static string SignCss<T>(this T value, string neutralClass = "")
        where T : INumber<T> =>
        value > T.Zero ? "text-success"
        : value < T.Zero ? "text-error"
        : neutralClass;

    public static string SignCss<T>(this T? value, string neutralClass = "")
        where T : struct, INumber<T> =>
        value.HasValue ? value.Value.SignCss(neutralClass) : neutralClass;

    public static string InverseSignCss<T>(this T value, string neutralClass = "")
        where T : INumber<T> =>
        value > T.Zero ? "text-error"
        : value < T.Zero ? "text-success"
        : neutralClass;

    public static string InverseSignCss<T>(this T? value, string neutralClass = "")
        where T : struct, INumber<T> =>
        value.HasValue ? value.Value.InverseSignCss(neutralClass) : neutralClass;
}
