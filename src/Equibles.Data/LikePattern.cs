namespace Equibles.Data;

public static class LikePattern
{
    // Escapes LIKE metacharacters ('\' '%' '_') so user input matches literally rather
    // than as wildcards. Use with EF.Functions.ILike(column, pattern, "\\").
    public static string Escape(string text)
    {
        return text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }

    // Escapes LIKE metacharacters and wraps in % for a contains match.
    // Use with EF.Functions.ILike(column, pattern, "\\").
    public static string Contains(string text)
    {
        return $"%{Escape(text)}%";
    }
}
