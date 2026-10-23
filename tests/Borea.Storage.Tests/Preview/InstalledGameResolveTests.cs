using System.Numerics;
using System.Xml;
using System.Xml.Linq;
using Borea.Core.Preview;
using Borea.Storage.Preview.Ksa;
using Borea.Storage.Preview.Sources;
using Tomlyn;
using Tomlyn.Model;
using Xunit.Abstractions;

namespace Borea.Storage.Tests.Preview;

/// <summary>
/// Resolves the stock content of an installed KSA with the preview rules, so a game update that changes the part
/// files shows up here first. The tests only read the install, and they run only when the environment variable
/// <see cref="InstalledGame.Variable"/> names a folder that holds Content/Core.
/// </summary>
/// <remarks>
/// The expected counts are for KSA 2026.10.19.5638 (<see cref="KsaPartSchema.VerifiedGameBuild"/>). The GameData checks
/// read the asset files a second time with plain XML and compare that with the merged catalog, the way
/// PartTemplate.ApplyGameData merges a GameData: subpart instances by ApplyOrAddSubPartInstance, render models by
/// Components.AddRange, and connectors with a new Id by Connectors.AddRange.
/// </remarks>
public sealed class InstalledGameResolveTests(InstalledGameResolveTests.CoreFixture core, ITestOutputHelper output)
    : IClassFixture<InstalledGameResolveTests.CoreFixture>
{
    [InstalledGameFact]
    public void TheDefaultVehicles_ResolveWithTheExpectedDraws_AndNoPlaceholder()
    {
        // The kitten vehicles hold only KittenBackPackPart, which has no render model, because the kitten model
        // comes from the Character and not from a part.
        var expected = new Dictionary<string, (int Min, int Max)>(StringComparer.Ordinal)
        {
            ["Gemini7"] = (280, 320),
            ["Rocket"] = (520, 560),
            ["Banjo"] = (0, 0),
            ["Hunter"] = (0, 0),
            ["Polaris"] = (0, 0),
        };

        var folder = Path.Combine(core.Value.CoreFolder, "defaultvehicles");
        var found = Directory.GetDirectories(folder).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), found);

        foreach (var name in found)
        {
            using var stream = File.OpenRead(Path.Combine(folder, name, VehicleXmlReader.FileName));
            var file = VehicleXmlReader.Read(stream);
            var vehicle = VehicleSceneResolver.Resolve(file.Parts, core.Value.Catalog, core.Value.Meshes, null);
            output.WriteLine($"{name}: {vehicle.PartCount} parts, {vehicle.Draws.Count} draws, {vehicle.Meshes.Count} meshes, {vehicle.Materials.Count} materials, {vehicle.Placeholders.Count} placeholders, {vehicle.SkippedInternalCount} skipped, {vehicle.Notices.Count} notices");

            Assert.Empty(file.Problems);
            Assert.Empty(vehicle.Notices);
            Assert.Empty(vehicle.Placeholders);
            Assert.InRange(vehicle.Draws.Count, expected[name].Min, expected[name].Max);
        }
    }

    [InstalledGameFact]
    public void EveryCorePartTemplate_ResolvesAsAOnePartVehicle_WithoutAPlaceholder()
    {
        var exceptions = new List<string>();
        var draws = 0;
        foreach (var id in core.Value.Xml.PartIds)
        {
            var vehicle = VehicleSceneResolver.Resolve([new VehiclePartRef(id, 1, Matrix4x4.Identity)], core.Value.Catalog, core.Value.Meshes, null);
            draws += vehicle.Draws.Count;
            exceptions.AddRange(vehicle.Placeholders.Select(placeholder => $"{id}: {placeholder.Reason} {placeholder.MissingId}"));
            exceptions.AddRange(vehicle.Notices.Select(notice => $"{id}: notice {notice.Kind} {notice.Subject}"));
        }

        output.WriteLine($"{core.Value.Xml.PartIds.Count} Part templates, {draws} draws in all");
        Assert.NotEmpty(core.Value.Xml.PartIds);
        Assert.True(exceptions.Count == 0, "These Part templates do not resolve:" + Environment.NewLine + string.Join(Environment.NewLine, exceptions));
    }

    [InstalledGameFact]
    public void TheCatalog_HasNoDuplicateId_AndEveryGameDataIsMergedIntoItsTemplate()
    {
        var catalog = core.Value.Catalog;
        var xml = core.Value.Xml;
        var duplicates = catalog.Notices.Count(notice => notice.Kind == PreviewNoticeKind.DuplicateId);
        var addsSubParts = 0;
        var addsModels = 0;
        var addsConnectors = 0;
        var attached = 0;
        var problems = new List<string>();

        foreach (var (id, gameData) in xml.GameData)
        {
            // ModLibrary.AttachGameData attaches only on an Id that is equal as a string.
            if (!catalog.TryGetTemplate(id, out var template) || !KsaIdComparer.Instance.Equals(template.Id, id))
                continue;

            attached++;
            var own = xml.Templates[id];
            var newSubParts = gameData.SubParts.Except(own.SubParts, KsaIdComparer.Instance).Count();
            var newConnectors = gameData.Connectors.Except(own.Connectors, KsaIdComparer.Instance).Count();
            addsSubParts += newSubParts > 0 ? 1 : 0;
            addsModels += gameData.Models > 0 ? 1 : 0;
            addsConnectors += newConnectors > 0 ? 1 : 0;

            if (template.RenderModels.Count != own.Models + gameData.Models)
                problems.Add($"{id}: {template.RenderModels.Count} render models, expected {own.Models + gameData.Models}");
            if (template.SubParts.Count != own.SubParts.Count + newSubParts)
                problems.Add($"{id}: {template.SubParts.Count} subparts, expected {own.SubParts.Count + newSubParts}");
            if (!template.Connectors.Select(connector => connector.Id).ToHashSet(KsaIdComparer.Instance).SetEquals(own.Connectors.Union(gameData.Connectors, KsaIdComparer.Instance)))
                problems.Add($"{id}: the connectors are not the ones of the template and its GameData");
        }

        output.WriteLine($"{catalog.TemplateCount} templates, {xml.GameData.Count} GameData Ids, {duplicates} DuplicateId notices, {catalog.Notices.Count} catalog notices, {core.Value.Meshes.Count} mesh names, {core.Value.Meshes.Notices.Count} mesh index notices");
        output.WriteLine($"GameData attached: {attached}, of them adding subparts: {addsSubParts}, render models: {addsModels}, new connectors: {addsConnectors}");

        Assert.Equal(0, duplicates);
        Assert.Empty(catalog.Notices);
        Assert.Empty(core.Value.Meshes.Notices);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));

        // The parachute bay gets its two parachutes only from its PartGameData.
        Assert.True(catalog.TryGetTemplate("CoreUtilityA_Prefab_ParachuteBayB", out var bay));
        Assert.Contains(bay.SubParts, subPart => subPart.Id == "CoreUtilityA_Subpart_StandaloneParachuteA1");
        Assert.Contains(bay.SubParts, subPart => subPart.Id == "CoreUtilityA_Subpart_StandaloneParachuteB1");

        // Every CoreFuelTankA part has each connector that CoreFuelTankAGameData names for it.
        var tanks = xml.GameData.Where(entry => entry.Value.File == "CoreFuelTankAGameData.xml").ToList();
        Assert.NotEmpty(tanks);
        foreach (var (id, gameData) in tanks)
        {
            Assert.True(catalog.TryGetTemplate(id, out var tank), id);
            Assert.Subset(tank.Connectors.Select(connector => connector.Id).ToHashSet(KsaIdComparer.Instance), gameData.Connectors.ToHashSet(KsaIdComparer.Instance));
        }
    }

    [InstalledGameFact]
    public void EveryCoreMeshAtlas_ListsAtLeastOneMeshName()
    {
        var atlases = Directory.GetFiles(core.Value.CoreFolder, "Core*_MeshAtlas.glb", SearchOption.AllDirectories);
        Assert.NotEmpty(atlases);

        foreach (var atlas in atlases.Order(StringComparer.Ordinal))
        {
            using var stream = File.OpenRead(atlas);
            var names = GlbContainer.Read(stream).MeshNames;
            output.WriteLine($"{Path.GetFileName(atlas)}: {names.Count} meshes");
            Assert.Contains(names, name => !string.IsNullOrEmpty(name));
        }
    }

    /// <summary>
    /// Builds the catalog and the mesh index of the game content once for all tests of the class. It builds nothing
    /// when the tests are skipped, because xunit can create a class fixture also then.
    /// </summary>
    public sealed class CoreFixture : IDisposable
    {
        private readonly Lazy<CoreContent> _value = new(() => new CoreContent(InstalledGame.Directory
            ?? throw new InvalidOperationException($"{InstalledGame.Variable} does not name a KSA install.")));

        internal CoreContent Value => _value.Value;

        public void Dispose()
        {
            if (_value.IsValueCreated)
                _value.Value.Dispose();
        }
    }

    internal sealed class CoreContent : IDisposable
    {
        private readonly GameCoreFiles _game;

        public CoreContent(string gameDirectory)
        {
            _game = GameCoreFiles.Open(gameDirectory);
            Assert.Equal(GameCoreStatus.Ok, _game.Status);

            CoreFolder = Path.Combine(gameDirectory, "Content", GameCoreFiles.CoreId);
            var mods = _game.Mods.Select(mod => new CatalogMod(mod.Key, mod.Value)).ToList();
            Catalog = PartCatalog.Build(mods, CancellationToken.None);
            Meshes = MeshIndex.Build(mods, Catalog.MeshFiles, CancellationToken.None);
            Xml = AssetXml.Read(Path.Combine(gameDirectory, "Content"), _game.Mods.Select(mod => mod.Key));
        }

        public string CoreFolder { get; }

        public PartCatalog Catalog { get; }

        public MeshIndex Meshes { get; }

        public AssetXml Xml { get; }

        public void Dispose()
        {
            foreach (var mod in _game.Mods)
                mod.Value.Dispose();
        }
    }

    /// <summary>The Ids that the asset files of the game content declare, read with plain XML as a check on the catalog.</summary>
    internal sealed class AssetXml
    {
        /// <summary>The Id of every Part element in load order. A Part with an Id that comes again is listed once.</summary>
        public List<string> PartIds { get; } = [];

        /// <summary>The first Part or SubPart element with each Id, as the shared registry keeps it.</summary>
        public Dictionary<string, Element> Templates { get; } = new(KsaIdComparer.Registry);

        /// <summary>Every PartGameData and SubPartGameData Id, with the elements of all GameData that have it folded together.</summary>
        public Dictionary<string, Element> GameData { get; } = new(KsaIdComparer.Registry);

        public static AssetXml Read(string contentFolder, IEnumerable<string> modIds)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            var result = new AssetXml();
            foreach (var modId in modIds)
            {
                var modFolder = Path.Combine(contentFolder, modId);
                var manifest = TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(Path.Combine(modFolder, "mod.toml")));
                var assets = manifest?["assets"] is TomlArray array ? array.OfType<string>() : [];
                foreach (var asset in assets)
                {
                    using var reader = XmlReader.Create(Path.Combine(modFolder, asset), settings);
                    foreach (var element in XDocument.Load(reader).Root!.Elements())
                        result.Add(element, asset);
                }
            }

            return result;
        }

        private void Add(XElement element, string file)
        {
            var id = (string?)element.Attribute(KsaPartSchema.Id) ?? string.Empty;
            if (id.Length == 0)
                return;

            switch (element.Name.LocalName)
            {
                case KsaPartSchema.Part or KsaPartSchema.SubPart:
                    if (Templates.TryAdd(id, Element.Of(element, file)) && element.Name.LocalName == KsaPartSchema.Part)
                        PartIds.Add(id);
                    break;
                case KsaPartSchema.PartGameData or KsaPartSchema.SubPartGameData:
                    GameData[id] = GameData.TryGetValue(id, out var first) ? first.Fold(Element.Of(element, file)) : Element.Of(element, file);
                    break;
            }
        }
    }

    /// <summary>What one template or GameData element holds: its subpart instance Ids, its render model count and its connector Ids.</summary>
    internal sealed record Element(string File, List<string> SubParts, int Models, List<string> Connectors)
    {
        public static Element Of(XElement element, string file)
        {
            var children = element.Elements().ToList();
            return new Element(
                file,
                [.. Ids(children, KsaPartSchema.SubPart)],
                children.Count(child => child.Name.LocalName is KsaPartSchema.PartModel or KsaPartSchema.PartModelDynamic or KsaPartSchema.PartModelGlass),
                [.. Ids(children, KsaPartSchema.Connector)]);
        }

        public Element Fold(Element later) =>
            this with { SubParts = [.. SubParts, .. later.SubParts], Models = Models + later.Models, Connectors = [.. Connectors, .. later.Connectors] };

        private static IEnumerable<string> Ids(IEnumerable<XElement> children, string name) =>
            children.Where(child => child.Name.LocalName == name).Select(child => (string?)child.Attribute(KsaPartSchema.Id) ?? string.Empty);
    }
}

/// <summary>Finds the KSA install that the opt-in preview tests read.</summary>
internal static class InstalledGame
{
    public const string Variable = "BOREA_PREVIEW_GAME_DIR";

    /// <summary>The folder that the variable names when it holds Content/Core, or null.</summary>
    public static string? Directory =>
        Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } folder
        && System.IO.Directory.Exists(Path.Combine(folder, "Content", GameCoreFiles.CoreId))
            ? folder
            : null;
}

/// <summary>A fact that runs only when <see cref="InstalledGame.Variable"/> names a KSA install, and is skipped otherwise.</summary>
internal sealed class InstalledGameFactAttribute : FactAttribute
{
    public InstalledGameFactAttribute()
    {
        if (InstalledGame.Directory is null)
            Skip = $"Set {InstalledGame.Variable} to a KSA install folder that holds Content/Core to run this test. It only reads the install.";
    }
}
