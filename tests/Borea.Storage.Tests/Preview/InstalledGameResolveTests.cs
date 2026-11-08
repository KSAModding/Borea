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
/// The expected counts are for KSA 2026.11.10.5801 (<see cref="KsaPartSchema.VerifiedGameBuild"/>). The merge checks
/// read the asset files a second time with plain XML and compare the patches and the Parent rows with the merged
/// catalog: an item that a list with Modify="Extend" adds or patches is in the template, and a variant that does not
/// write a list has the list of its parent.
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
            ["Gemini7"] = (260, 300),
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
    public void TheHoseMeshAndMaterial_ResolveInTheCore_AndAStockFuelPortHasItsConnector()
    {
        var catalog = core.Value.Catalog;

        Assert.True(core.Value.Meshes.TryFind(KsaPartSchema.HoseMesh, out _));
        Assert.True(catalog.TryGetMaterial(KsaPartSchema.HoseMaterial, out var material));
        Assert.NotNull(material.Diffuse);
        var ports = core.Value.Xml.PartIds
            .Select(id => catalog.TryGetTemplate(id, out var template) ? template : null)
            .Where(template => template is { FuelPorts.Count: > 0 })
            .ToList();
        Assert.NotEmpty(ports);
        Assert.All(ports, template => Assert.Contains(template!.SubParts, instance => instance.Id == template.FuelPorts[0].ConnectorSubPartId));
    }

    [InstalledGameFact]
    public void TheCatalog_HasNoNotice_AndEveryPatchAndParentIsMergedIntoItsTemplate()
    {
        var catalog = core.Value.Catalog;
        var xml = core.Value.Xml;
        var problems = new List<string>();
        var checkedItems = 0;

        foreach (var patch in xml.Patches)
        {
            var id = (string)patch.Attribute(KsaPartSchema.Id)!;
            if (!catalog.TryGetTemplate(id, out var template))
            {
                problems.Add($"{id}: the patched template is not in the catalog");
                continue;
            }

            foreach (var key in AddedKeys(patch, KsaPartSchema.SubParts))
            {
                checkedItems++;
                if (!template.SubParts.Any(instance => instance.Id == key))
                    problems.Add($"{id}: subpart {key} of a patch is missing");
            }

            foreach (var key in AddedKeys(patch, KsaPartSchema.Connectors))
            {
                checkedItems++;
                if (!template.Connectors.Any(connector => connector.Id == key))
                    problems.Add($"{id}: connector {key} of a patch is missing");
            }

            foreach (var model in RenderModelKeys(patch))
            {
                checkedItems++;
                if (!template.RenderModels.Any(existing => existing.ModelId == model))
                    problems.Add($"{id}: render model {model} of a patch is missing");
            }
        }

        foreach (var (id, parentId, writes) in xml.Variants)
        {
            Assert.True(catalog.TryGetTemplate(id, out var variant), id);
            Assert.True(catalog.TryGetTemplate(parentId, out var parent), parentId);
            checkedItems++;
            writes.UnionWith(xml.Patches
                .Where(patch => KsaIdComparer.Registry.Equals((string?)patch.Attribute(KsaPartSchema.Id), id))
                .SelectMany(patch => patch.Elements().Select(child => child.Name.LocalName)));
            if (!writes.Contains(KsaPartSchema.Connectors) && !variant.Connectors.SequenceEqual(parent.Connectors))
                problems.Add($"{id}: the connectors are not the ones of its parent {parentId}");
            if (!writes.Contains(KsaPartSchema.SubParts) && !variant.SubParts.SequenceEqual(parent.SubParts))
                problems.Add($"{id}: the subparts are not the ones of its parent {parentId}");
        }

        output.WriteLine($"{catalog.TemplateCount} templates, {xml.Patches.Count} patch rows, {xml.Variants.Count} variants, {checkedItems} items checked, {catalog.Notices.Count} catalog notices, {core.Value.Meshes.Count} mesh names, {core.Value.Meshes.Notices.Count} mesh index notices");

        Assert.Empty(catalog.Notices);
        Assert.Empty(core.Value.Meshes.Notices);
        Assert.NotEmpty(xml.Patches);
        Assert.NotEmpty(xml.Variants);
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));

        // The parachute bay gets its two parachutes only from the SubParts that its patch extends.
        Assert.True(catalog.TryGetTemplate("CoreUtilityA_Prefab_ParachuteBayA", out var bay));
        Assert.Contains(bay.SubParts, subPart => subPart.Id == "CoreUtilityA_Subpart_StandaloneParachuteA1");
        Assert.Contains(bay.SubParts, subPart => subPart.Id == "CoreUtilityA_Subpart_StandaloneParachuteB1");
    }

    /// <summary>The Keys of the items without Modify and with Modify="Patch" in a list that the patch extends.</summary>
    private static IEnumerable<string> AddedKeys(XElement patch, string list) =>
        ExtendedItems(patch, list)
            .Select(item => (string?)item.Attribute(KsaPartSchema.Key))
            .OfType<string>();

    /// <summary>The Keys of the render models in the Components that the patch extends.</summary>
    private static IEnumerable<string> RenderModelKeys(XElement patch) =>
        ExtendedItems(patch, KsaPartSchema.Components)
            .Where(item => item.Name.LocalName is KsaPartSchema.PartModel or KsaPartSchema.PartModelDynamic or KsaPartSchema.PartModelGlass)
            .Select(item => (string?)item.Attribute(KsaPartSchema.Key))
            .OfType<string>();

    private static IEnumerable<XElement> ExtendedItems(XElement patch, string list) =>
        patch.Elements(list)
            .Where(element => (string?)element.Attribute(KsaPartSchema.Modify) == "Extend")
            .Elements()
            .Where(item => (string?)item.Attribute(KsaPartSchema.Modify) is null or "Patch");

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

    /// <summary>The Part and SubPart rows of the asset files of the game content, read with plain XML as a check on the catalog.</summary>
    internal sealed class AssetXml
    {
        private readonly HashSet<string> _seen = new(KsaIdComparer.Registry);

        /// <summary>The Id of every Part row without Modify in load order. A Part with an Id that comes again is listed once.</summary>
        public List<string> PartIds { get; } = [];

        /// <summary>The Part and SubPart rows with Modify="Patch".</summary>
        public List<XElement> Patches { get; } = [];

        /// <summary>The Part rows with Parent, with the names of the elements that each one writes itself.</summary>
        public List<(string Id, string ParentId, HashSet<string> Writes)> Variants { get; } = [];

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
                        result.Add(element);
                }
            }

            return result;
        }

        private void Add(XElement element)
        {
            var id = (string?)element.Attribute(KsaPartSchema.Id) ?? string.Empty;
            if (id.Length == 0 || element.Name.LocalName is not (KsaPartSchema.Part or KsaPartSchema.SubPart))
                return;

            if ((string?)element.Attribute(KsaPartSchema.Modify) == "Patch")
            {
                Patches.Add(element);
                return;
            }

            if (element.Name.LocalName == KsaPartSchema.Part && _seen.Add(id))
                PartIds.Add(id);

            if ((string?)element.Attribute(KsaPartSchema.Parent) is { } parent)
                Variants.Add((id, parent, element.Elements().Select(child => child.Name.LocalName).ToHashSet(StringComparer.Ordinal)));
        }
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
