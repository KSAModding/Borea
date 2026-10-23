using System.Globalization;
using System.Text;

namespace Borea.Core.Stewardship;

/// <summary>A line diff of two texts as unified diff hunks, the form of the patch GitHub sends for a changed file.</summary>
internal static class LineDiff
{
    private const int Context = 3;

    /// <summary>Above this many line pairs between the common start and end, the changed part shows as removed and added whole.</summary>
    private const long MaxPairs = 4_000_000;

    /// <returns>The hunks, each with its @@ line, or an empty text when both texts are the same.</returns>
    public static string Unified(string before, string after)
    {
        var lines = Script(Lines(before), Lines(after));
        var patch = new StringBuilder();
        var index = 0;
        while (FirstChange(lines, index) is { } first)
        {
            var last = first;
            while (FirstChange(lines, last + 1) is { } next && next - last - 1 <= 2 * Context)
                last = next;

            var start = Math.Max(first - Context, 0);
            var end = Math.Min(last + Context, lines.Count - 1);
            if (patch.Length > 0)
                patch.Append('\n');
            AppendHunk(patch, lines, start, end);

            index = end + 1;
        }

        return patch.ToString();
    }

    /// <summary>Appends the @@ line of the lines from <paramref name="start"/> to <paramref name="end"/>, and those lines.</summary>
    private static void AppendHunk(StringBuilder patch, List<(char Kind, string Text)> lines, int start, int end)
    {
        var hunk = lines.GetRange(start, end - start + 1);
        var oldBefore = lines.Take(start).Count(line => line.Kind != '+');
        var newBefore = lines.Take(start).Count(line => line.Kind != '-');
        patch.Append("@@ -").Append(Range(oldBefore, hunk.Count(line => line.Kind != '+')))
            .Append(" +").Append(Range(newBefore, hunk.Count(line => line.Kind != '-'))).Append(" @@");
        foreach (var (kind, text) in hunk)
            patch.Append('\n').Append(kind).Append(text);
    }

    /// <summary>The lines of a text, without the empty one after a final line break.</summary>
    private static List<string> Lines(string text)
    {
        var lines = text.Split('\n').ToList();
        if (lines is [.., ""])
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    /// <summary>Every line as kept, removed or added, with a longest common subsequence between the common start and end.</summary>
    private static List<(char Kind, string Text)> Script(List<string> before, List<string> after)
    {
        var head = 0;
        while (head < before.Count && head < after.Count && before[head] == after[head])
            head++;

        var tail = 0;
        while (tail < before.Count - head && tail < after.Count - head && before[^(tail + 1)] == after[^(tail + 1)])
            tail++;

        var lines = before.Take(head).Select(line => (' ', line)).ToList();
        var removed = before.GetRange(head, before.Count - head - tail);
        var added = after.GetRange(head, after.Count - head - tail);
        if ((long)removed.Count * added.Count > MaxPairs)
        {
            lines.AddRange(removed.Select(line => ('-', line)));
            lines.AddRange(added.Select(line => ('+', line)));
        }
        else
        {
            AppendCommonSubsequence(lines, removed, added);
        }

        lines.AddRange(before.Skip(before.Count - tail).Select(line => (' ', line)));
        return lines;
    }

    /// <summary>The changed part as kept, removed or added lines along a longest common subsequence.</summary>
    private static void AppendCommonSubsequence(List<(char Kind, string Text)> lines, List<string> removed, List<string> added)
    {
        var common = new int[removed.Count + 1, added.Count + 1];
        for (var i = removed.Count - 1; i >= 0; i--)
        {
            for (var j = added.Count - 1; j >= 0; j--)
                common[i, j] = removed[i] == added[j] ? common[i + 1, j + 1] + 1 : Math.Max(common[i + 1, j], common[i, j + 1]);
        }

        var (a, b) = (0, 0);
        while (a < removed.Count && b < added.Count)
        {
            if (removed[a] == added[b])
            {
                lines.Add((' ', removed[a++]));
                b++;
            }
            else if (common[a + 1, b] >= common[a, b + 1])
            {
                lines.Add(('-', removed[a++]));
            }
            else
            {
                lines.Add(('+', added[b++]));
            }
        }

        lines.AddRange(removed.Skip(a).Select(line => ('-', line)));
        lines.AddRange(added.Skip(b).Select(line => ('+', line)));
    }

    private static int? FirstChange(List<(char Kind, string Text)> lines, int from)
    {
        for (var index = from; index < lines.Count; index++)
        {
            if (lines[index].Kind != ' ')
                return index;
        }

        return null;
    }

    /// <summary>A range of a hunk header: the first line and the count, or the line before an empty range.</summary>
    private static string Range(int linesBefore, int count) =>
        (count == 0 ? linesBefore : linesBefore + 1).ToString(CultureInfo.InvariantCulture) + "," + count.ToString(CultureInfo.InvariantCulture);
}
