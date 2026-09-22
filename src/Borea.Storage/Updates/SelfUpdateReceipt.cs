using Borea.Core.Updates;

namespace Borea.Storage.Updates;

/// <summary>
/// The note that a staging build leaves in the folder of the new build. It names the program the new
/// build may remove and carries the token of the handover, so that a handover which no Borea build
/// staged removes nothing.
/// </summary>
internal static class SelfUpdateReceipt
{
    /// <summary>The receipt in the folder of the new build. Its first line is the token and its second line the replaced program.</summary>
    public const string FileName = "borea-update-receipt.txt";

    /// <summary>Writes the receipt of one staged build. The caller turns a failure into a failed handover.</summary>
    public static void Write(string folder, string previousProgramPath, string token)
        => File.WriteAllLines(Path.Combine(folder, FileName), [token, previousProgramPath]);

    /// <summary>Whether <paramref name="folder"/> holds the receipt that belongs to this handover.</summary>
    public static bool Matches(string folder, SelfUpdateHandover handover, string previousProgramPath)
    {
        try
        {
            return File.ReadAllLines(Path.Combine(folder, FileName)) is [var token, var previous, ..]
                && string.Equals(token, handover.Token, StringComparison.Ordinal)
                && string.Equals(Path.GetFullPath(previous), previousProgramPath, SelfUpdateCleanup.PathComparison);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Removes a receipt that was used, because it is good for one handover only.</summary>
    public static void Delete(string folder)
    {
        try
        {
            File.Delete(Path.Combine(folder, FileName));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
