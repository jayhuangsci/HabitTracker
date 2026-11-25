using System.Text;

namespace PromptCrew.Core;

public static class TextComparison
{
    // Exact line-oriented replacement, bounded without quadratic allocation for long prompts.
    public static string Diff(string left, string right)
    {
        var a = left.Replace("\r\n", "\n").Split('\n');
        var b = right.Replace("\r\n", "\n").Split('\n');
        var start = 0;
        while (start < Math.Min(a.Length, b.Length) && a[start] == b[start]) start++;
        var endA = a.Length; var endB = b.Length;
        while (endA > start && endB > start && a[endA - 1] == b[endB - 1]) { endA--; endB--; }
        if (start == a.Length && start == b.Length) return "内容相同";
        var result = new StringBuilder();
        foreach (var line in a.Take(start).TakeLast(3)) result.Append("  ").AppendLine(line);
        foreach (var line in a[start..endA]) result.Append("− ").AppendLine(line);
        foreach (var line in b[start..endB]) result.Append("+ ").AppendLine(line);
        foreach (var line in a.Skip(endA).Take(3)) result.Append("  ").AppendLine(line);
        return result.ToString();
    }
}
