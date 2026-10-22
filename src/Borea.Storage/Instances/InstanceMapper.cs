using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Storage.Mods;

namespace Borea.Storage.Instances;

public static class InstanceMapper
{
    public static InstanceDto ToDto(Instance instance)
    {
        var (sourceType, modPackId, modPackVersion) = instance.Source switch
        {
            InstanceSource.FromModPack mp => ("ModPack", mp.ModPackId, mp.Version.ToString()),
            InstanceSource.Custom => ("Custom", null, null),
            _ => throw new NotSupportedException($"Unknown InstanceSource type '{instance.Source.GetType().Name}'."),
        };

        return new InstanceDto
        {
            InstanceId = instance.InstanceId.ToString(),
            Name = instance.Name,
            CreatedAt = instance.CreatedAt,
            IsFavorite = instance.IsFavorite,
            LastPlayedAt = instance.LastPlayedAt,
            LaunchArguments = instance.LaunchArguments.ToList(),
            LoaderId = instance.Loader.LoaderId,
            LoaderVersion = instance.Loader.Version?.ToString(),
            SourceType = sourceType,
            SourceModPackId = modPackId,
            SourceModPackVersion = modPackVersion,
            SourceModPackDetached = instance.Source is InstanceSource.FromModPack { Detached.Count: > 0 } pack
                ? pack.Detached.Order(ModIds.Comparer).ToList()
                : null,
            Mods = instance.Mods.Select(InstalledModMapper.ToDto).ToList(),
            ForeignMods = instance.ForeignMods.Select(ForeignModMapper.ToDto).ToList(),
        };
    }

    public static Instance FromDto(InstanceDto dto)
    {
        if (dto is null) throw new ArgumentNullException(nameof(dto));

        var instanceId = Guid.Parse(dto.InstanceId);

        InstanceSource source = dto.SourceType switch
        {
            "ModPack" => new InstanceSource.FromModPack(
                dto.SourceModPackId ?? throw new FormatException("ModPack source is missing SourceModPackId."),
                ModVersion.Parse(dto.SourceModPackVersion ?? throw new FormatException("ModPack source is missing SourceModPackVersion.")))
                .WithDetached(dto.SourceModPackDetached ?? []),
            "Custom" => InstanceSource.Custom.Value,
            _ => throw new FormatException($"Unknown SourceType '{dto.SourceType}'."),
        };

        var mods = dto.Mods.Select(InstalledModMapper.FromDto).ToList();
        var foreignMods = dto.ForeignMods.Select(ForeignModMapper.FromDto).ToList();

        var instance = Instance.FromExisting(instanceId, dto.Name, source, dto.CreatedAt, mods, foreignMods, dto.IsFavorite, dto.LastPlayedAt, dto.LaunchArguments);
        instance.SetLoader(LoaderFrom(dto));
        return instance;
    }

    // a hand-edited loader that does not parse falls back to the default, because a start can still choose a loader then
    private static LoaderSetting LoaderFrom(InstanceDto dto)
    {
        if (!ModIds.IsValid(dto.LoaderId))
            return LoaderSetting.Automatic;

        if (dto.LoaderVersion is null)
            return LoaderSetting.ForLoader(dto.LoaderId!);

        return ModVersion.TryParse(dto.LoaderVersion, out var version)
            ? LoaderSetting.Pinned(dto.LoaderId!, version)
            : LoaderSetting.Automatic;
    }
}
