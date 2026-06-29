namespace Equibles.Data;

public static class LikePattern
{
    // The escape character baked into patterns by Escape/Contains; pass it as the
    // EF.Functions.ILike escape argument so '\' '%' '_' match literally.
    public const string EscapeChar = "\\";

    // Escapes LIKE metacharacters ('\' '%' '_') so user input matches literally rather
    // than as wildcards. Use with EF.Functions.ILike(column, pattern, LikePattern.EscapeChar).
    public static string Escape(string text)
    {
        return text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }

    // Escapes LIKE metacharacters and wraps in % for a contains match.
    // Use with EF.Functions.ILike(column, pattern, LikePattern.EscapeChar).
    public static string Contains(string text)
    {
        return $"%{Escape(text)}%";
    }

    // Escapes LIKE metacharacters and appends % for a prefix (starts-with) match.
    // Use with EF.Functions.ILike(column, pattern, LikePattern.EscapeChar).
    public static string StartsWith(string text)
    {
        return $"{Escape(text)}%";
    }
}
