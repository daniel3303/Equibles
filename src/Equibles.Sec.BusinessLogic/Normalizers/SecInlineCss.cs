namespace Equibles.Sec.BusinessLogic.Normalizers;

// Shared inline-CSS matching for SEC HTML normalization. Both HeadingConversionStep
// and ListConversionStep need to detect a CSS declaration inside a raw style/markup
// string, tolerating SEC EDGAR's two emitted forms.
internal static class SecInlineCss
{
    // SEC EDGAR emits inline CSS with and without a space after the colon
    // (e.g. "font-weight:bold" and "font-weight: bold"); both forms must match.
    public static bool ContainsDeclaration(string source, string property, string value)
    {
        return source.Contains($"{property}:{value}") || source.Contains($"{property}: {value}");
    }
}
