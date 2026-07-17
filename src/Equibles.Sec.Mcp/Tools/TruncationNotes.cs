using System.Text;
using Equibles.Mcp.Helpers;

namespace Equibles.Sec.Mcp.Tools;

/// <summary>
/// Appends <see cref="McpOutput.TruncationNote"/> under a rendered markdown table. The leading
/// blank line is load-bearing: without it a strict CommonMark renderer absorbs the note into the
/// table as a stray single-cell row. No-op when nothing was cut off.
/// </summary>
internal static class TruncationNotes
{
    internal static void Append(StringBuilder result, int shown, int total)
    {
        var note = McpOutput.TruncationNote(shown, total);
        if (note.Length > 0)
        {
            result.AppendLine();
            result.AppendLine(note);
        }
    }
}
