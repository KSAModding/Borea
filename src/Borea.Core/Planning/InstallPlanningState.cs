using System.Security.Cryptography;
using System.Text;
using Borea.Core.Dependencies;
using Borea.Core.Instances;
using Borea.Core.Mods;

namespace Borea.Core.Planning;

public sealed record InstallPlanningState(string Value)
{
    public static InstallPlanningState Capture(Instance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var value = new StringBuilder();
        Add(value, instance.InstanceId.ToString("D"));
        Add(value, Source(instance.Source));
        Add(value, instance.Mods.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var mod in instance.Mods.OrderBy(item => item.ModId, ModIds.Comparer))
        {
            Add(value, "managed");
            Add(value, mod.ModId);
            Add(value, mod.Version.ToString());
            Add(value, mod.Reason.ToString());
            Add(value, mod.Ownership.ToString());
            Add(value, mod.OwnershipToken);
            Add(value, mod.Checksum);
            Add(value, mod.Metadata.ModId);
            Add(value, mod.Metadata.Version.ToString());
            Add(value, mod.Metadata.GameMinRevision.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Add(value, mod.Metadata.GameMaxRevision?.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Add(value, mod.Metadata.Os?.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var platform in mod.Metadata.Os ?? []) Add(value, platform);
            Add(value, mod.Metadata.Yanked ? "yanked" : "available");
            Add(value, mod.Metadata.YankedReason);
            AddDependencies(value, mod.Metadata.Dependencies);
        }
        Add(value, instance.ForeignMods.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var mod in instance.ForeignMods.OrderBy(item => item.ModId, ModIds.Comparer))
        {
            Add(value, "foreign");
            Add(value, mod.ModId);
            Add(value, mod.DependencyReadError);
            Add(value, mod.Dependencies.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var dependency in mod.Dependencies.OrderBy(item => item.ModId, StringComparer.Ordinal).ThenBy(item => item.Optional))
            {
                Add(value, dependency.ModId);
                Add(value, dependency.Optional ? "optional" : "required");
            }
        }
        return new InstallPlanningState(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString()))));
    }

    public bool Matches(Instance instance) => this == Capture(instance);

    private static void AddDependencies(StringBuilder value, IReadOnlyList<ModDependency> dependencies)
    {
        Add(value, dependencies.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var dependency in dependencies)
        {
            Add(value, dependency.Kind.ToString());
            Add(value, dependency.Source?.ToString());
            if (dependency.IsAnyOf)
            {
                Add(value, "any-of");
                Add(value, dependency.AnyOf.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
                foreach (var alternative in dependency.AnyOf)
                {
                    Add(value, alternative.ModId);
                    Add(value, alternative.MinVersion?.ToString());
                    Add(value, alternative.MaxVersion?.ToString());
                }
            }
            else
            {
                Add(value, dependency.ModId);
                Add(value, dependency.MinVersion?.ToString());
                Add(value, dependency.MaxVersion?.ToString());
            }
        }
    }

    private static string Source(InstanceSource source) => source switch
    {
        InstanceSource.Custom => "custom",
        InstanceSource.FromModPack pack => $"pack:{pack.ModPackId}:{pack.Version}",
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };

    private static void Add(StringBuilder value, string? field)
    {
        if (field is null)
        {
            value.Append("-1:");
            return;
        }
        value.Append(field.Length).Append(':').Append(field);
    }
}
