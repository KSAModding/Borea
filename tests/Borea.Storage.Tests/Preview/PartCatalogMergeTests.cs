using System.Numerics;
using Borea.Core.Preview;
using Borea.Storage.Preview.Ksa;

namespace Borea.Storage.Tests.Preview;

/// <summary>The part layout of KSA 2026.11.10.5801: lists in SubParts, Connectors and Components, Key and Ref, Texture assets, and the Modify and Parent merge of rows.</summary>
public sealed class PartCatalogMergeTests : IDisposable
{
    private readonly CatalogTestMods _mods = new();

    public void Dispose() => _mods.Dispose();

    [Fact]
    public void AGeneratedPartAndItsPatch_GiveSubPartsConnectorsModelsFuelPortAndTank()
    {
        const string assets = """
            <SubPart Id="Skin"><Components><PartModel Key="SkinModel"><Mesh Ref="SkinMesh" /><Material Ref="Hull" /></PartModel><MeshView><Mesh Ref="SkinMesh_VM" /></MeshView></Components></SubPart>
            <SubPart Id="Plug" />
            <Part Id="Tank">
              <SubParts>
                <SubPart Key="Skin1" Ref="Skin"><Transform><Position X="1" /></Transform></SubPart>
                <SubPart Key="Plug1" Ref="Plug" />
              </SubParts>
              <Connectors>
                <Connector Key="top"><Transform><Position X="0.5" /></Transform></Connector>
                <Connector Key="bottom"><Transform><Position X="-0.5" /></Transform></Connector>
              </Connectors>
            </Part>
            """;
        const string gameData = """
            <Part Id="Tank" Modify="Patch" DisplayName="Tank">
              <EditorTags Modify="Extend"><EditorTag Ref="Fuel Tanks" /></EditorTags>
              <Connectors Modify="Extend">
                <Connector Key="top" Modify="Patch"><Capabilities>BulkFluid</Capabilities></Connector>
                <Connector Key="side"><Transform><Position Y="2" /></Transform></Connector>
              </Connectors>
              <Components Modify="Extend">
                <PartModelGlass Key="Window"><Mesh Ref="GlassMesh" /></PartModelGlass>
                <FuelPort ConnectorMaxAngle="60"><SubPart KeyRef="Plug1" /><ConnectorTip Z="-0.5" /></FuelPort>
                <Tank><CylindricalTank /></Tank>
              </Components>
            </Part>
            """;
        var core = _mods.Mod("Core", ["Assets.xml", "GameData.xml"], ("Assets.xml", CatalogTestMods.Assets(assets)), ("GameData.xml", CatalogTestMods.Assets(gameData)));

        var catalog = PartCatalog.Build([core], CancellationToken.None);

        Assert.Empty(catalog.Notices);
        Assert.True(catalog.TryGetTemplate("Tank", out var tank));
        Assert.Equal([("Skin1", "Skin"), ("Plug1", "Plug")], tank.SubParts.Select(instance => (instance.Id, instance.InstanceOf)));
        Assert.Equal(new Vector3(1, 0, 0), tank.SubParts[0].Transform?.Position);
        Assert.Equal([new ConnectorData("top", new Vector3(0.5f, 0, 0)), new ConnectorData("bottom", new Vector3(-0.5f, 0, 0)), new ConnectorData("side", new Vector3(0, 2, 0))], tank.Connectors);
        Assert.Equal((PreviewModelKind.Glass, "Window", "GlassMesh"), (Assert.Single(tank.RenderModels).Kind, tank.RenderModels[0].ModelId, tank.RenderModels[0].MeshId));
        Assert.Equal(new FuelPortData("Plug1", Vector3.Zero, float.DegreesToRadians(60f), Vector3.Zero, new Vector3(0, 0, -0.5f), 0.15f), Assert.Single(tank.FuelPorts));
        Assert.True(tank.HasTank);
        Assert.True(catalog.TryGetTemplate("Skin", out var skin) && skin.IsSubPart);
        Assert.Equal(("SkinModel", "SkinMesh", "Hull"), (Assert.Single(skin.RenderModels).ModelId, skin.RenderModels[0].MeshId, skin.RenderModels[0].MaterialKey));
    }

    [Fact]
    public void AVariant_InheritsWhatItDoesNotWrite_ReplacesTheListsItWrites_AndAnAbstractParentIsNoTemplate()
    {
        const string assets = """
            <Part Id="Base" Abstract="true">
              <SubParts><SubPart Key="a" Ref="A" /><SubPart Key="b" Ref="B" /></SubParts>
              <Connectors><Connector Key="top"><Transform><Position X="1" /></Transform></Connector></Connectors>
              <Components><PartModel Key="m"><Mesh Ref="BaseMesh" /></PartModel></Components>
            </Part>
            <Part Id="Short" Parent="Base"><SubParts><SubPart Key="c" Ref="C" /></SubParts></Part>
            <Part Id="Long" Parent="Base"><SubParts Modify="Extend"><SubPart Key="c" Ref="C" /><SubPart Key="a" Modify="Remove" /></SubParts></Part>
            <Part Id="Base" Modify="Patch"><Components Modify="Extend"><PartModel Key="n"><Mesh Ref="PatchMesh" /></PartModel></Components></Part>
            """;
        var core = _mods.Mod("Core", ["A.xml"], ("A.xml", CatalogTestMods.Assets(assets)));

        var catalog = PartCatalog.Build([core], CancellationToken.None);

        Assert.Empty(catalog.Notices);
        Assert.False(catalog.TryGetTemplate("Base", out _));
        Assert.True(catalog.TryGetTemplate("Short", out var shortPart));
        Assert.True(catalog.TryGetTemplate("Long", out var longPart));
        Assert.Equal(["c"], shortPart.SubParts.Select(instance => instance.Id));
        Assert.Equal(["b", "c"], longPart.SubParts.Select(instance => instance.Id));
        Assert.Equal(["top"], shortPart.Connectors.Select(connector => connector.Id));
        Assert.Equal(["BaseMesh", "PatchMesh"], longPart.RenderModels.Select(model => model.MeshId));
        Assert.Equal(("Short", "Long"), (shortPart.Id, longPart.Id));
    }

    [Fact]
    public void MaterialSlots_NameTextureAssets_WhosePathsResolveInTheModThatDefinesThem()
    {
        const string core = """
            <Texture Id="HullDiffuse" Path="Textures/hull.ktx2" Category="Vessel" />
            <Texture Id="HullNormal" Path="Textures/hull_n.ktx2" />
            <PbrMaterial Id="Hull"><Diffuse Ref="HullDiffuse" /><Normal Ref="HullNormal" /><AoRoughMetal Ref="NoSuchTexture" /></PbrMaterial>
            """;
        const string mod = """
            <Texture Id="HullNormal" Modify="Patch" Path="Textures/better_n.ktx2" />
            <Texture Id="Own" Path="Textures/own.ktx2" />
            <PbrMaterial Id="Hull" Modify="Patch"><AoRoughMetal Ref="Own" /></PbrMaterial>
            """;
        CatalogMod[] mods = [_mods.Mod("Core", ["A.xml"], ("A.xml", CatalogTestMods.Assets(core))), _mods.Mod("Mod", ["A.xml"], ("A.xml", CatalogTestMods.Assets(mod)))];

        var catalog = PartCatalog.Build(mods, CancellationToken.None);

        Assert.Empty(catalog.Notices);
        Assert.True(catalog.TryGetMaterial("Hull", out var hull));
        Assert.Equal(
            new MaterialData("Hull", new PreviewAssetRef("Core", "Textures/hull.ktx2"), new PreviewAssetRef("Mod", "Textures/own.ktx2"), new PreviewAssetRef("Core", "Textures/better_n.ktx2")),
            hull);
    }

    [Theory]
    [InlineData("""<Part Id="P" Modify="Replace"><Components><PartModel Key="m"><Mesh Ref="Replaced" /></PartModel></Components></Part>""", "Replaced")]
    [InlineData("""<Part Id="p" Modify="patch"><Components Modify="Extend"><PartModel Key="m" Modify="Patch"><Mesh Ref="Patched" /></PartModel></Components></Part>""", "Patched")]
    [InlineData("""<Part Id="P" Modify="Patch"><Components Modify="Extend"><PartModel Key="m" Modify="Replace"><Mesh Ref="ItemReplaced" /></PartModel></Components></Part>""", "ItemReplaced")]
    [InlineData("""<Part Id="P" Modify="Patch"><Components><PartModel Key="x"><Mesh Ref="ListReplaced" /></PartModel></Components></Part>""", "ListReplaced")]
    [InlineData("""<Part Id="P" Modify="Remove" /><Part Id="P"><Components><PartModel Key="m"><Mesh Ref="Again" /></PartModel></Components></Part>""", "Again")]
    [InlineData("""<Part Id="Q" Modify="PatchIfExists" /><Part Id="Q" Modify="RemoveIfExists" /><Part Id="Q" Modify="ReplaceIfExists" />""", "Generated")]
    public void ARowVerb_ChangesTheRowWithItsTagAndId_FromAnotherMod(string change, string mesh)
    {
        CatalogMod[] mods =
        [
            _mods.Mod("Core", ["A.xml"], ("A.xml", CatalogTestMods.Assets("""<Part Id="P"><Components><PartModel Key="m"><Mesh Ref="Generated" /></PartModel></Components></Part>"""))),
            _mods.Mod("Mod", ["A.xml"], ("A.xml", CatalogTestMods.Assets(change))),
        ];

        var catalog = PartCatalog.Build(mods, CancellationToken.None);

        Assert.Empty(catalog.Notices);
        Assert.True(catalog.TryGetTemplate("P", out var part));
        Assert.Equal(mesh, Assert.Single(part.RenderModels).MeshId);
        Assert.False(catalog.TryGetTemplate("Q", out _));
    }

    [Fact]
    public void ARemovedRow_IsGone_AndAPatchOfIt_IsReported()
    {
        const string assets = """
            <Part Id="P" />
            <Part Id="P" Modify="Remove" />
            <Part Id="P" Modify="Patch" DisplayName="Late" />
            """;
        var core = _mods.Mod("Core", ["A.xml"], ("A.xml", CatalogTestMods.Assets(assets)));

        var catalog = PartCatalog.Build([core], CancellationToken.None);

        Assert.False(catalog.TryGetTemplate("P", out _));
        Assert.Equal((PreviewNoticeKind.AssetFailed, "Core/A.xml"), (Assert.Single(catalog.Notices).Kind, catalog.Notices[0].Subject));
    }

    [Theory]
    [InlineData("""<Part Id="P" Modify="Patch"><Connectors Modify="Extend"><Connector Key="missing" Modify="Patch" /></Connectors><Components Modify="Extend"><PartModel Key="n"><Mesh Ref="Lost" /></PartModel></Components></Part>""")]
    [InlineData("""<Part Id="P" Modify="Patch"><Connectors Modify="Extend"><Connector Key="top" /></Connectors></Part>""")]
    [InlineData("""<Part Id="P" Modify="Patch"><Gimbal Modify="Remove" /></Part>""")]
    [InlineData("""<Part Id="P" Modify="Patch"><Connectors Modify="Merge" /></Part>""")]
    public void APatchThatDoesNotApply_LeavesTheRowAsItWas_WithANotice(string patch)
    {
        const string part = """<Part Id="P"><Connectors><Connector Key="top" /></Connectors><Components><PartModel Key="m"><Mesh Ref="Kept" /></PartModel></Components></Part>""";
        var core = _mods.Mod("Core", ["A.xml"], ("A.xml", CatalogTestMods.Assets(part + patch)));

        var catalog = PartCatalog.Build([core], CancellationToken.None);

        Assert.True(catalog.TryGetTemplate("P", out var template));
        Assert.Equal(["Kept"], template.RenderModels.Select(model => model.MeshId));
        Assert.Equal(["top"], template.Connectors.Select(connector => connector.Id));
        Assert.Equal(PreviewNoticeKind.AssetFailed, Assert.Single(catalog.Notices).Kind);
    }

    [Fact]
    public void ParentsThatLeadBackToThemselves_AreLeftOutWithACycleNotice_AndASecondDefinitionWithADuplicateNotice()
    {
        const string assets = """
            <Part Id="A" Parent="B" />
            <Part Id="B" Parent="A" />
            <Part Id="C" Parent="Missing" />
            <Part Id="D"><Components><PartModel Key="m"><Mesh Ref="First" /></PartModel></Components></Part>
            <Part Id="d"><Components><PartModel Key="m"><Mesh Ref="Second" /></PartModel></Components></Part>
            """;
        var core = _mods.Mod("Core", ["A.xml"], ("A.xml", CatalogTestMods.Assets(assets)));

        var catalog = PartCatalog.Build([core], CancellationToken.None);

        Assert.False(catalog.TryGetTemplate("A", out _) || catalog.TryGetTemplate("B", out _) || catalog.TryGetTemplate("C", out _));
        Assert.True(catalog.TryGetTemplate("D", out var d));
        Assert.Equal("First", Assert.Single(d.RenderModels).MeshId);
        Assert.Equal(
            [PreviewNoticeKind.DuplicateId, PreviewNoticeKind.AssetFailed, PreviewNoticeKind.Cycle],
            catalog.Notices.Select(notice => notice.Kind).Order());
    }

    [Fact]
    public void ARowWithAValueThatCannotBeRead_IsLeftOut_AndTheOtherRowsOfTheFileStay()
    {
        const string assets = """
            <Part Id="Bad"><Components><PartModel Key="m"><Mesh Ref="M" /><RayTracing>Sometimes</RayTracing></PartModel></Components></Part>
            <Part Id="Good"><Components><PartModel Key="m"><Mesh Ref="M" /><RayTracing>ShadowProxy</RayTracing></PartModel></Components></Part>
            """;
        var core = _mods.Mod("Core", ["A.xml"], ("A.xml", CatalogTestMods.Assets(assets)));

        var catalog = PartCatalog.Build([core], CancellationToken.None);

        Assert.False(catalog.TryGetTemplate("Bad", out _));
        Assert.True(catalog.TryGetTemplate("Good", out var good) && good.RenderModels[0].ShadowProxy);
        Assert.Equal((PreviewNoticeKind.AssetFailed, "Core/A.xml"), (Assert.Single(catalog.Notices).Kind, catalog.Notices[0].Subject));
    }

    [Fact]
    public void ATextureAndAMeshAtlasShareTheRegistryOfFiles_SoTheLaterOneIsLeftOut()
    {
        const string assets = """
            <Texture Id="Shared" Path="Textures/a.ktx2" />
            <MeshAtlas Id="shared" Path="Meshes/a.glb" />
            <MeshAtlas Id="Atlas" Path="Meshes/b.glb" HostOnly="true" />
            """;
        var core = _mods.Mod("Core", ["A.xml"], ("A.xml", CatalogTestMods.Assets(assets)));

        var catalog = PartCatalog.Build([core], CancellationToken.None);

        Assert.Equal(new MeshFileDeclaration(MeshFileKind.Atlas, new PreviewAssetRef("Core", "Meshes/b.glb"), "Atlas", true), Assert.Single(catalog.MeshFiles));
        Assert.Equal((PreviewNoticeKind.DuplicateId, "shared"), (Assert.Single(catalog.Notices).Kind, catalog.Notices[0].Subject));
    }
}
