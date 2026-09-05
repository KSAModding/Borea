using Borea.Core.Dependencies;

namespace Borea.Storage.Mods;

public static class ModDependencyMapper
{
    public static ModDependencyDto ToDto(ModDependency dependency) => new()
    {
        ModId = dependency.ModId,
        Kind = MetadataEnumMapper.ToDto(dependency.Kind),
        MinVersion = dependency.MinVersion?.ToString(),
        MaxVersion = dependency.MaxVersion?.ToString(),
        Source = MetadataEnumMapper.ToDto(dependency.Source),
        AnyOf = dependency.AnyOf?.Select(ModDependencyAlternativeMapper.ToDto).ToList(),
    };

    public static ModDependency FromDto(ModDependencyDto dto)
    {
        var kind = MetadataEnumMapper.ParseKind(dto.Kind);
        var source = MetadataEnumMapper.ParseSource(dto.Source);

        if (dto.AnyOf is not null)
        {
            if (dto.AnyOf.Count == 0)
                throw new FormatException("An any_of list cannot be empty.");

            if (dto.ModId is not null)
                throw new FormatException("A dependency entry carries a mod id or an any_of list, not both.");

            if (kind is not (ModDependencyKind.Required or ModDependencyKind.Recommends))
                throw new FormatException($"An any_of dependency entry cannot have kind '{dto.Kind}'.");

            return ModDependency.OfAlternatives(kind, dto.AnyOf.Select(ModDependencyAlternativeMapper.FromDto).ToList(), source);
        }

        if (dto.ModId is null)
            throw new FormatException("A dependency entry needs a mod id or an any_of list.");

        return new ModDependency(dto.ModId, kind, MetadataEnumMapper.ParseVersion(dto.MinVersion), MetadataEnumMapper.ParseVersion(dto.MaxVersion), source);
    }
}
