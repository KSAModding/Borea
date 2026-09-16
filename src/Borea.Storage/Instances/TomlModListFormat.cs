using Borea.Core.Instances;
using Borea.Core.Mods;
using Tomlyn;

namespace Borea.Storage.Instances;

/// <summary>
/// Keys this version does not know are ignored, so an optional key needs no new format version.
/// </summary>
public sealed class TomlModListFormat : IModListFormat
{
    private static readonly TomlSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = TomlIgnoreCondition.WhenWritingNull,
    };

    public string Write(ModList modList)
    {
        ArgumentNullException.ThrowIfNull(modList);

        var dto = new ModListDto
        {
            Format = ModList.CurrentFormat,
            Name = modList.Name,
            Mods = modList.Mods
                .Select(entry => new ModListEntryDto { Id = entry.ModId, Version = entry.Version.ToString(), Enabled = entry.Enabled })
                .ToList(),
        };
        return TomlSerializer.Serialize(dto, Options);
    }

    public ModList Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        ModListDto? dto;
        try
        {
            dto = TomlSerializer.Deserialize<ModListDto>(text, Options);
        }
        catch (TomlException exception)
        {
            throw new FormatException($"The file is not valid TOML. {exception.Message}", exception);
        }

        if (dto?.Format is not { } format)
            throw new FormatException("The file has no format key, so it is not a Borea modlist.");
        if (format > ModList.CurrentFormat)
            throw new UnsupportedModListFormatException(format);
        if (format < 1)
            throw new FormatException($"The file has format {format}, which no modlist uses.");

        var entries = new List<ModListEntry>(dto.Mods.Count);
        foreach (var entry in dto.Mods)
        {
            if (!ModIds.IsValid(entry.Id))
                throw new FormatException($"'{entry.Id}' is not a valid content id.");
            if (entry.Version is null || !ModVersion.TryParse(entry.Version, out var version))
                throw new FormatException($"'{entry.Version}' of '{entry.Id}' is not a valid semantic version.");

            entries.Add(new ModListEntry(entry.Id!, version, entry.Enabled));
        }

        var duplicate = entries.GroupBy(entry => entry.ModId, ModIds.Comparer).FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicate is not null)
            throw new FormatException($"The file names '{duplicate}' more than once.");

        return new ModList(dto.Name, entries);
    }
}
