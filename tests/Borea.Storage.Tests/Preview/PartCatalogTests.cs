using System.Numerics;
using Borea.Core.Preview;
using Borea.Storage.Preview.Ksa;
using Borea.Storage.Preview.Sources;

namespace Borea.Storage.Tests.Preview;

public sealed class PartCatalogTests : IDisposable
{
    private readonly CatalogTestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    [Fact]
    public void TheAssetsListSetsTheOrder_AndAnUnlistedMissingOrBrokenFileAddsNothing()
    {
        var core = _mods.Mod("Core", ["Second.xml", "Missing.xml", "Broken.xml", "First.xml"],
            ("Second.xml", CatalogTestMods.Assets(Part("P", "FromSecond"))),
            ("First.xml", CatalogTestMods.Assets(Part("P", "FromFirst") + Part("Q", "Q"))),
            ("Broken.xml", "<Assets><Part Id=\"B\"></Assets>"),
            ("Unlisted.xml", CatalogTestMods.Assets(Part("U", "U"))));

        var catalog = PartCatalog.Build([core], CancellationToken.None);

        Assert.Equal("FromSecond", MeshOf(catalog, "P"));
        Assert.True(catalog.TryGetTemplate("Q", out _));
        Assert.False(catalog.TryGetTemplate("B", out _));
        Assert.False(catalog.TryGetTemplate("U", out _));
        Assert.Contains(catalog.Notices, notice => notice is { Kind: PreviewNoticeKind.AssetFailed, Subject: "Core/Missing.xml" });
        Assert.Contains(catalog.Notices, notice => notice is { Kind: PreviewNoticeKind.AssetFailed, Subject: "Core/Broken.xml" });
        Assert.Contains(catalog.Notices, notice => notice is { Kind: PreviewNoticeKind.DuplicateId, Subject: "P" });
    }

    [Theory]
    [InlineData("Part", "Part_x", "Part", "Part_x", false)]
    [InlineData("Part", "Part_x", "Part", "PART_x", true)]
    [InlineData("SubPart", "X", "Part", "x", true)]
    public void ADuplicateTemplateId_KeepsTheFirst_AcrossModsCaseAndKinds(string firstElement, string firstId, string secondElement, string secondId, bool sameMod)
    {
        var first = Template(firstElement, firstId, "First");
        var second = Template(secondElement, secondId, "Second");
        CatalogMod[] mods = sameMod
            ? [_mods.Mod("Core", ["A.xml"], ("A.xml", CatalogTestMods.Assets(first + second)))]
            : [_mods.Mod("Core", ["A.xml"], ("A.xml", CatalogTestMods.Assets(first))), _mods.Mod("Later", ["A.xml"], ("A.xml", CatalogTestMods.Assets(second)))];

        var catalog = PartCatalog.Build(mods, CancellationToken.None);

        Assert.True(catalog.TryGetTemplate(secondId, out var template));
        Assert.Equal(firstId, template.Id);
        Assert.Equal("First", Assert.Single(template.RenderModels).MeshId);
        Assert.Contains(catalog.Notices, notice => notice is { Kind: PreviewNoticeKind.DuplicateId } && notice.Subject == secondId);
    }

    [Fact]
    public void GameData_MergesSubPartsConnectorsAndModels_TheWayTheGameDoes()
    {
        const string assets = """
            <SubPart Id="Skin"><PartModel Id="m"><Mesh Id="SkinMesh" /></PartModel></SubPart>
            <SubPart Id="Cap" />
            <Part Id="Tank">
              <SubPart Id="A" InstanceOf="Skin"><Transform><Position X="1" /><Rotation Z="0.5" /></Transform></SubPart>
              <SubPart Id="Bare" InstanceOf="Skin" />
              <Connector Id="top"><Transform><Position X="0.5" /></Transform></Connector>
            </Part>
            """;
        const string gameData = """
            <PartGameData Id="Tank">
              <SubPart Id="A"><Transform><Position X="2" /></Transform></SubPart>
              <SubPart Id="Bare"><Transform><Position X="9" /></Transform></SubPart>
              <SubPart Id="B" InstanceOf="Cap" />
              <Connector Id="top"><Transform><Position X="7" /></Transform></Connector>
              <Connector Id="bottom"><Transform><Position X="-3" Y="1" /></Transform></Connector>
            </PartGameData>
            <SubPartGameData Id="Cap"><PartModel Id="capModel"><Mesh Id="CapMesh" /></PartModel></SubPartGameData>
            <SubPart Id="Bolt" />
            """;
        var core = _mods.Mod("Core", ["Assets.xml", "GameData.xml"],
            ("Assets.xml", CatalogTestMods.Assets(assets)),
            ("GameData.xml", CatalogTestMods.Assets(gameData)));

        var catalog = PartCatalog.Build([core], CancellationToken.None);

        Assert.True(catalog.TryGetTemplate("Tank", out var tank));
        Assert.Collection(tank.SubParts,
            a => Assert.Equal(new KsaTransform(new Vector3(2, 0, 0), new Vector3(0, 0, 0.5f), null), a.Transform),
            bare => Assert.Null(bare.Transform),
            b => Assert.Equal(("B", "Cap"), (b.Id, b.InstanceOf)));
        Assert.Equal([new ConnectorData("top", new Vector3(0.5f, 0, 0)), new ConnectorData("bottom", new Vector3(-3, 1, 0))], tank.Connectors);
        Assert.Equal(new PartExtents(new Vector3(-3, 0, 0), new Vector3(0.5f, 1, 0)), tank.ConnectorExtents);
        Assert.Equal("CapMesh", MeshOf(catalog, "Cap"));
        Assert.True(catalog.TryGetTemplate("Bolt", out var bolt) && bolt.IsSubPart);
    }

    [Fact]
    public void PartGameDataWithOneId_FoldsAcrossMods_AndAttachesOnlyOnAnExactId()
    {
        var core = _mods.Mod("Core", ["A.xml"], ("A.xml", CatalogTestMods.Assets(Template("Part", "P", null) + Template("Part", "Q", null))));
        var first = _mods.Mod("First", ["A.xml"], ("A.xml", CatalogTestMods.Assets(Template("PartGameData", "P", "FromFirst"))));
        var second = _mods.Mod("Second", ["A.xml"], ("A.xml", CatalogTestMods.Assets(Template("PartGameData", "p", "FromSecond") + Template("PartGameData", "q", "Lost"))));

        var catalog = PartCatalog.Build([core, first, second], CancellationToken.None);

        Assert.True(catalog.TryGetTemplate("P", out var p));
        Assert.Equal(["FromFirst", "FromSecond"], p.RenderModels.Select(model => model.MeshId));
        Assert.True(catalog.TryGetTemplate("Q", out var q));
        Assert.Empty(q.RenderModels);
    }

    [Fact]
    public void Materials_ResolveAcrossMods_InlineOnesGetAStableKey_AndModelFlagsAndPatchesAreRead()
    {
        const string coreAssets = """
            <PbrMaterial Id="Hull"><Diffuse Path="Textures/hull.ktx2" /><Normal Path="Textures/hull_n.ktx2" /><AoRoughMetal Path="Textures\hull_pbr.ktx2" /></PbrMaterial>
            """;
        const string modAssets = """
            <Part Id="P">
              <PartModel Id="a"><Mesh Id="m" /><Material Id="hull" /><Internal>true</Internal></PartModel>
              <PartModel Id="b"><Mesh Id="m" /><Material><Diffuse Path="Textures/own.png" /></Material><RayTracing>ShadowProxy</RayTracing></PartModel>
              <PartModelGlass Id="c"><Mesh Id="m" /></PartModelGlass>
            </Part>
            """;
        CatalogMod[] mods =
        [
            _mods.Mod("Core", ["A.xml"], ("A.xml", CatalogTestMods.Assets(coreAssets))),
            _mods.Mod("Mod", ["A.xml"], "patches = [\"p.xml\"]", ("A.xml", CatalogTestMods.Assets(modAssets))),
        ];

        var catalog = PartCatalog.Build(mods, CancellationToken.None);
        var again = PartCatalog.Build(mods, CancellationToken.None);

        Assert.True(catalog.TryGetTemplate("P", out var part));
        var (reference, inline, glass) = (part.RenderModels[0], part.RenderModels[1], part.RenderModels[2]);
        Assert.True(catalog.TryGetMaterial(reference.MaterialKey!, out var hull));
        Assert.Equal(new MaterialData("Hull", new PreviewAssetRef("Core", "Textures/hull.ktx2"), new PreviewAssetRef("Core", "Textures/hull_pbr.ktx2"), new PreviewAssetRef("Core", "Textures/hull_n.ktx2")), hull);
        Assert.Equal((true, false), (reference.Internal, reference.ShadowProxy));
        Assert.True(catalog.TryGetMaterial(inline.MaterialKey!, out var own));
        Assert.Equal(new PreviewAssetRef("Mod", "Textures/own.png"), own.Diffuse);
        Assert.Equal(inline.MaterialKey, again.TryGetTemplate("P", out var rebuilt) ? rebuilt.RenderModels[1].MaterialKey : null);
        Assert.Equal((false, true), (inline.Internal, inline.ShadowProxy));
        Assert.Equal((PreviewModelKind.Glass, null), (glass.Kind, glass.MaterialKey));
        Assert.Contains(catalog.Notices, notice => notice is { Kind: PreviewNoticeKind.PatchMod, Subject: "Mod" });
    }

    [Fact]
    public void AFuelPort_HasTheValuesOfItsElement_OrTheDefaultsOfTheGame_AndAGameDataAddsItAfterThePortOfThePart()
    {
        const string assets = """
            <Part Id="P">
              <FuelPort MaxLength="100" Connector="Plug" ConnectorMaxAngle="60" ConnectorLeadOut="0.5">
                <AnchorAsmb X="1" Y="2" Z="3" />
                <ConnectorPivot Z="0.5" />
                <ConnectorTip Z="-0.5" />
              </FuelPort>
            </Part>
            <PartGameData Id="P"><FuelPort Id="Second" /><Tank Id="t" /></PartGameData>
            <Part Id="Q" />
            """;
        var core = _mods.Mod("Core", ["Parts.xml"], ("Parts.xml", CatalogTestMods.Assets(assets)));

        var catalog = PartCatalog.Build([core], CancellationToken.None);

        Assert.Empty(catalog.Notices);
        Assert.True(catalog.TryGetTemplate("P", out var part));
        Assert.Equal(2, part.FuelPorts.Count);
        Assert.True(part.HasTank);
        Assert.Equal(new FuelPortData("Plug", new Vector3(1, 2, 3), float.DegreesToRadians(60f), new Vector3(0, 0, 0.5f), new Vector3(0, 0, -0.5f), 0.5f), part.FuelPorts[0]);
        Assert.Equal(new FuelPortData(null, Vector3.Zero, float.DegreesToRadians(10f), Vector3.Zero, Vector3.Zero, 0.15f), part.FuelPorts[1]);
        Assert.True(catalog.TryGetTemplate("Q", out var plain));
        Assert.Empty(plain.FuelPorts);
        Assert.False(plain.HasTank);
    }

    [Theory]
    [InlineData("""<FuelPort ConnectorMaxAngle="wide" />""")]
    [InlineData("""<FuelPort ConnectorLeadOut="0,15" />""")]
    [InlineData("""<FuelPort><AnchorAsmb X="far" /></FuelPort>""")]
    public void AFuelPortWithABadNumber_IsLeftOut_AndTheFileKeepsItsParts(string port)
    {
        var assets = $"""
            <Part Id="P">
              {port}
              <Tank Id="t" />
            </Part>
            <Part Id="Q" />
            """;
        var core = _mods.Mod("Core", ["Parts.xml"], ("Parts.xml", CatalogTestMods.Assets(assets)));

        var catalog = PartCatalog.Build([core], CancellationToken.None);

        Assert.Empty(catalog.Notices);
        Assert.True(catalog.TryGetTemplate("P", out var part));
        Assert.Empty(part.FuelPorts);
        Assert.True(part.HasTank);
        Assert.True(catalog.TryGetTemplate("Q", out _));
    }

    private static string Part(string id, string mesh) => Template("Part", id, mesh);

    private static string Template(string element, string id, string? mesh) =>
        mesh is null
            ? $"<{element} Id=\"{id}\" />"
            : $"<{element} Id=\"{id}\"><PartModel Id=\"model\"><Mesh Id=\"{mesh}\" /></PartModel></{element}>";

    private static string? MeshOf(PartCatalog catalog, string id)
    {
        Assert.True(catalog.TryGetTemplate(id, out var template));
        return Assert.Single(template.RenderModels).MeshId;
    }
}

/// <summary>Writes small mod folders with a mod.toml and asset files into a temporary folder.</summary>
internal sealed class CatalogTestMods : IDisposable
{
    private readonly List<IPreviewFiles> _files = [];

    public string Root { get; } = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "BoreaTest_" + Guid.NewGuid())).FullName;

    public static string Assets(string body) => $"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<Assets>\n{body}\n</Assets>\n";

    public CatalogMod Mod(string id, string[] assets, params (string Path, string Text)[] files) => Mod(id, assets, string.Empty, files);

    public CatalogMod Mod(string id, string[] assets, string extraToml, params (string Path, string Text)[] files)
    {
        var folder = Folder(id);
        var list = string.Join(", ", assets.Select(asset => $"\"{asset}\""));
        File.WriteAllText(Path.Combine(folder, "mod.toml"), $"name = \"{id}\"\nassets = [{list}]\n{extraToml}\n");
        foreach (var (path, text) in files)
            Write(id, path, System.Text.Encoding.UTF8.GetBytes(text));

        return Add(id, new FolderPreviewFiles(folder));
    }

    public CatalogMod Add(string id, IPreviewFiles files)
    {
        _files.Add(files);
        return new CatalogMod(id, files);
    }

    public string Folder(string id) => Directory.CreateDirectory(Path.Combine(Root, id)).FullName;

    public void Write(string id, string path, byte[] bytes)
    {
        var full = Path.Combine(Folder(id), path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
    }

    public void Dispose()
    {
        foreach (var files in _files)
            files.Dispose();

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
