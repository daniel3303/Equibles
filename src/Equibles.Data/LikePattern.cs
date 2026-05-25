namespace Equibles.Data;

public static class LikePattern
{
    // Escapes LIKE metacharacters and wraps in % for a contains match.
    // Use with EF.Functions.ILike(column, pattern, "\\").
    public static string Contains(string text)
    {
        var escaped = text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        return $"%{escaped}%";
    }
}
