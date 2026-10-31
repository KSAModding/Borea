using Borea.Core.Sharing;
using Borea.Storage.Mods;
using Borea.Storage.Tests.Paths;

namespace Borea.Storage.Tests.Mods;

public sealed class FileContentProviderMapTests : IDisposable
{
    private const string CoreAssets = """
        <?xml version="1.0" encoding="utf-8"?>
        <Assets>
          <Part Id="CoreCommandPod"><Components><TankData InstanceOf="NotAnId" /></Components></Part>
          <SubPart Id="CoreAntenna" />
          <Character Id="EvaKitten" />
          <Substance Id="LOX"><Name Value="Oxygen" /><Liquid /><Gas /></Substance>
          <StellarBody Id="Sol" />
          <TerrestrialBody Id="Earth" />
          <Texture Id="NotAnIdEither" />
        </Assets>
        """;

    private const string CoreSystem = """
        <?xml version="1.0" encoding="utf-8"?>
        <System Id="Sol">
          <LoadFromLibrary Id="Earth" Parent="Sol" />
          <TerrestrialBody Id="Moonlet" />
        </System>
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid());
    private readonly TestGamePathProvider _paths;
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly FileContentProviderMap _map;

    public FileContentProviderMapTests()
    {
        _paths = new TestGamePathProvider(_root);
        _map = new FileContentProviderMap(_paths);
        var content = Path.Combine(_paths.GetGameDirectoryPath()!, "Content");
        Write(Path.Combine(content, "manifest.toml"), "[[mods]]\nid = \"Core\"\nenabled = true\n");
        Write(Path.Combine(content, "Core", "mod.toml"), "name = \"Core\"\nassets = [\"Assets.xml\"]\nsystems = [\"Sol.xml\"]\n");
        Write(Path.Combine(content, "Core", "Assets.xml"), CoreAssets);
        Write(Path.Combine(content, "Core", "Sol.xml"), CoreSystem);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task ReadAsync_GameContent_GivesItsIdsByKind()
    {
        var core = Assert.Single((await _map.ReadAsync(_instanceId)).Mods);

        Assert.Equal(("Core", true, true), (core.FolderName, core.Enabled, core.IsGameContent));
        Assert.Equal(["CoreCommandPod", "CoreAntenna"], core.Defines.Of(ContentIdKind.Part));
        Assert.Equal(["EvaKitten"], core.Defines.Of(ContentIdKind.Character));
        Assert.Equal(["LOX(l)", "LOX(g)"], core.Defines.Of(ContentIdKind.SubstancePhase));
        Assert.Equal(["Sol", "Earth", "Moonlet"], core.Defines.Of(ContentIdKind.Body));
        Assert.Equal(["Sol"], core.Defines.Of(ContentIdKind.System));
    }

    [Fact]
    public async Task ReadAsync_Manifest_KeepsTheLoadOrderAndReadsContentBeforeMods()
    {
        Manifest(("Core", true), ("Wheels", true), ("Seats", false));
        Mod(Path.Combine(_paths.GetGameDirectoryPath()!, "Content"), "Wheels", "<Part Id=\"ContentWheel\" />");
        Mod(_paths.GetInstanceModsFolder(_instanceId), "Wheels", "<Part Id=\"ModsWheel\" />");
        Mod(_paths.GetInstanceModsFolder(_instanceId), "Seats", "<Part Id=\"Seat\" />");
        Mod(_paths.GetInstanceModsFolder(_instanceId), "Antennas", "<Part Id=\"Antenna\" />");

        var mods = (await _map.ReadAsync(_instanceId)).Mods;

        Assert.Equal(["Core", "Wheels", "Seats", "Antennas"], mods.Select(mod => mod.FolderName));
        Assert.Equal(["ContentWheel"], mods[1].Defines.Of(ContentIdKind.Part));
        Assert.Equal([true, true, false, false], mods.Select(mod => mod.Enabled));
        Assert.Equal([true, false, false, false], mods.Select(mod => mod.IsGameContent));
    }

    [Fact]
    public async Task ReadAsync_BrokenAssetFile_IsNamedAndTheRestIsRead()
    {
        Manifest(("Core", true), ("Wheels", true));
        var folder = Mod(_paths.GetInstanceModsFolder(_instanceId), "Wheels", "<Part Id=\"Wheel\" />");
        Write(Path.Combine(folder, "mod.toml"), "assets = [\"Parts.xml\", \"Broken.xml\", \"Missing.xml\"]\n");
        Write(Path.Combine(folder, "Broken.xml"), "<Assets><Part Id=\"Half\"");

        var providers = await _map.ReadAsync(_instanceId);

        Assert.Equal(["Wheel"], providers.Mods[1].Defines.Of(ContentIdKind.Part));
        Assert.Equal([Path.Combine(folder, "Broken.xml"), Path.Combine(folder, "Missing.xml")], providers.UnreadableFiles);
    }

    private void Manifest(params (string Id, bool Enabled)[] entries) =>
        Write(_paths.GetInstanceManifestPath(_instanceId), string.Concat(entries.Select(entry => $"[[mods]]\nid = \"{entry.Id}\"\nenabled = {(entry.Enabled ? "true" : "false")}\n")));

    private static string Mod(string root, string id, string parts)
    {
        var folder = Path.Combine(root, id);
        Write(Path.Combine(folder, "mod.toml"), $"name = \"{id}\"\nassets = [\"Parts.xml\"]\n");
        Write(Path.Combine(folder, "Parts.xml"), $"<Assets>{parts}</Assets>");
        return folder;
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
