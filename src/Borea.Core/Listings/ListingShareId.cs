using System.Text;

namespace Borea.Core.Listings;

/// <summary>The listing id the client proposes for a shared vehicle or save, from the name in its meta.toml (RFC 0085).</summary>
public static class ListingShareId
{
    public const int MaxLength = 64;

    /// <summary>The ASCII letters and digits of the name, at most <see cref="MaxLength"/>. German letters become ae, oe, ue and ss, a letter whose accent Unicode can split off keeps its base letter, and any other letter is left out. Empty when the name has none.</summary>
    public static string From(string? name)
    {
        var mapped = new StringBuilder();
        foreach (var c in name ?? string.Empty)
        {
            mapped.Append(c switch
            {
                (char)0xE4 => "ae",
                (char)0xF6 => "oe",
                (char)0xFC => "ue",
                (char)0xDF => "ss",
                (char)0xC4 => "Ae",
                (char)0xD6 => "Oe",
                (char)0xDC => "Ue",
                (char)0x1E9E => "SS",
                _ => c.ToString(),
            });
        }

        var letters = mapped.ToString().Normalize(NormalizationForm.FormD)
            .Where(char.IsAsciiLetterOrDigit)
            .Take(MaxLength)
            .ToArray();
        return new string(letters);
    }
}
