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
            SourceType = sourceType,
            SourceModPackId = modPackId,
            SourceModPackVersion = modPackVersion,
            Mods = instance.Mods.Select(InstalledModMapper.ToDto).ToList(),
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
                ModVersion.Parse(dto.SourceModPackVersion ?? throw new FormatException("ModPack source is missing SourceModPackVersion."))),
            "Custom" => InstanceSource.Custom.Value,
            _ => throw new FormatException($"Unknown SourceType '{dto.SourceType}'."),
        };

        var mods = dto.Mods.Select(InstalledModMapper.FromDto).ToList();

        return Instance.FromExisting(instanceId, dto.Name, source, dto.CreatedAt, mods, dto.IsFavorite);
    }
}