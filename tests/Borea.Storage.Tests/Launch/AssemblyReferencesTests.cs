using Borea.Core.Instances;
using Borea.Core.Launch;
using Borea.Core.Mods;
using Borea.Storage.Launch;
using Borea.Storage.Tests.Mods;

namespace Borea.Storage.Tests.Launch;

public sealed class AssemblyReferencesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("Brutal.Core.Numerics")]
    [InlineData("brutal.core.numerics")]
    public void Referencing_DllInASubfolderThatReferencesTheName_IsTrue(string name)
    {
        ReferencingAssembly.Write(Path.Combine(_root, "bin", "KSArmory.dll"), "KSA", "Brutal.Core.Numerics");

        Assert.True(AssemblyReferences.Referencing(_root, [name]));
    }

    [Fact]
    public void Referencing_OtherNames_IsFalse()
    {
        ReferencingAssembly.Write(Path.Combine(_root, "KSArmory.dll"), "KSA", "Brutal.Core.Numerics");

        Assert.False(AssemblyReferences.Referencing(_root, ["Brutal.Core.Logging", "Brutal.Core"]));
    }

    [Fact]
    public void Referencing_NameTheFolderHoldsItself_IsFalse()
    {
        ReferencingAssembly.Write(Path.Combine(_root, "Compendium.dll"), "ModMenu.Attributes");
        ReferencingAssembly.Write(Path.Combine(_root, "ModMenu.Attributes.dll"));

        Assert.False(AssemblyReferences.Referencing(_root, ["ModMenu.Attributes"]));
    }

    [Fact]
    public void Referencing_FilesThatAreNoDotnetDll_AreSkipped()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(Path.Combine(_root, "empty.dll"), []);
        File.WriteAllText(Path.Combine(_root, "notes.dll"), "Brutal.Core.Numerics");
        ReferencingAssembly.Write(Path.Combine(_root, "KSArmory.dll"), "Brutal.Core.Numerics");

        Assert.True(AssemblyReferences.Referencing(_root, ["Brutal.Core.Numerics"]));
        File.Delete(Path.Combine(_root, "KSArmory.dll"));
        Assert.False(AssemblyReferences.Referencing(_root, ["Brutal.Core.Numerics"]));
    }

    [Fact]
    public void Referencing_MissingFolderOrNoNames_IsFalse()
    {
        ReferencingAssembly.Write(Path.Combine(_root, "KSArmory.dll"), "Brutal.Core.Numerics");

        Assert.False(AssemblyReferences.Referencing(Path.Combine(_root, "gone"), ["Brutal.Core.Numerics"]));
        Assert.False(AssemblyReferences.Referencing(_root, []));
    }

    [Fact]
    public void Mods_GivesTheEnabledCodeModsThatReferenceTheName_InLoadOrder()
    {
        var instance = new Instance("Main", InstanceSource.Custom.Value);
        foreach (var modId in new[] { "AdvancedFlightComputer", "Compendium", "DeltaVMap", "ModMenu", "LonelyPlanets" })
        {
            var release = MetadataFixtures.MinimalRelease(modId, "1.0.0");
            instance.AddMod(new InstalledMod(modId, release.Version, InstallReason.Manual, DateTimeOffset.UtcNow, release));
        }

        ReferencingAssembly.Write(Path.Combine(_root, "AdvancedFlightComputer", "AdvancedFlightComputer.dll"), "Brutal.Core.Numerics");
        ReferencingAssembly.Write(Path.Combine(_root, "Compendium", "Compendium.dll"), "Brutal.Core.Numerics");
        ReferencingAssembly.Write(Path.Combine(_root, "DeltaVMap", "DeltaVMap.dll"), "Brutal.Core.Numerics");
        ReferencingAssembly.Write(Path.Combine(_root, "ModMenu", "ModMenu.dll"), "Brutal.Core.Logging");
        Directory.CreateDirectory(Path.Combine(_root, "LonelyPlanets"));
        LoadOrderMod[] loadOrder =
        [
            new("Core", IsCodeMod: false),
            new("DeltaVMap", IsCodeMod: true),
            new("Compendium", IsCodeMod: true),
            new("ModMenu", IsCodeMod: true),
            new("LonelyPlanets", IsCodeMod: false),
        ];

        var mods = AssemblyReferences.Mods(instance, _root, loadOrder, ["Brutal.Core.Numerics"]);

        Assert.Equal(["DeltaVMap", "Compendium"], mods.Select(mod => mod.ModId));
    }
}
