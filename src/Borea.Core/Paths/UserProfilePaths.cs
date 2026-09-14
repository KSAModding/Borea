using System.Text;

namespace Borea.Core.Paths;

/// <summary>
/// Replaces the user profile folder with "~", so a log or a bug report does
/// not carry the user name.
/// </summary>
public static class UserProfilePaths
{
    public static string Hide(string text)
        => Hide(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>
    /// Replaces every <paramref name="profile"/> in <paramref name="text"/> that
    /// is not followed by a letter or digit, so a sibling folder such as the
    /// profile name plus "2" stays as it is.
    /// </summary>
    public static string Hide(string text, string? profile)
    {
        ArgumentNullException.ThrowIfNull(text);

        var folder = profile?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrEmpty(folder))
            return text;

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var result = new StringBuilder(text.Length);
        var start = 0;
        int index;
        while ((index = text.IndexOf(folder, start, comparison)) >= 0)
        {
            var end = index + folder.Length;
            result.Append(text, start, index - start);
            if (end < text.Length && char.IsLetterOrDigit(text[end]))
                result.Append(text, index, folder.Length);
            else
                result.Append('~');
            start = end;
        }

        return result.Append(text, start, text.Length - start).ToString();
    }
}
