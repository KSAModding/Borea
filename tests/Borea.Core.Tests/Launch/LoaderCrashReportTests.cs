using Borea.Core.Launch;

namespace Borea.Core.Tests.Launch;

public sealed class LoaderCrashReportTests
{
    [Fact]
    public void AssemblyNames_FindsTheAssembliesTheExceptionsName_InOrderAndOnce()
    {
        string[] output =
        [
            "StarMap - Using Instance Path: C:\\Instances\\Main",
            "Method 'DrawAxes' in type 'KSArmory.RoundFollowable' from assembly 'KSArmory, Version=0.8.44.0, Culture=neutral, PublicKeyToken=null' does not have an implementation.",
            "System.IO.FileNotFoundException: Could not load file or assembly 'MeasureTools, Version=1.1.10.0'.",
            "Method 'DrawAxes' in type 'KSArmory.Other' from assembly 'KSArmory, Version=0.8.44.0' does not have an implementation.",
        ];

        Assert.Equal(["KSArmory", "MeasureTools"], LoaderCrashReport.AssemblyNames(output));
    }

    [Fact]
    public void AssemblyNames_OutputWithoutAssemblies_IsEmpty()
    {
        Assert.Empty(LoaderCrashReport.AssemblyNames(["Unhandled exception. System.NullReferenceException", ""]));
    }

    private static readonly string[] ClrCrash =
    [
        "Fatal error.",
        "Internal CLR error. (0x80131506)",
        "   at System.Reflection.RuntimeModule.GetDefinedTypes()",
        "   at System.Reflection.RuntimeModule.GetTypes()",
        "   at StarMap.Core.ModRepository.RuntimeMod.InitializeMod(StarMap.Core.ModRepository.ModRegistry)",
        "   at StarMap.Core.ModRepository.ModLoader.PrepareMods()",
        "   at StarMap.Core.ModRepository.ModLoader.Init()",
    ];

    private static string[] Output(params string[] reports) =>
        [@"StarMap - Using Instance Path: C:\Users\Player\Borea\Instances\1ec0f20c", .. reports, .. ClrCrash];

    private static LoadOrderMod Code(string modId) => new(modId, IsCodeMod: true);

    [Fact]
    public void LikelyLoadingMod_TwoModsLoadedBeforeTheCrash_IsTheThirdCodeMod()
    {
        var output = Output("StarMap - Loaded mod: ModMenu from manifest", "StarMap - Loaded mod: MeasureTools from manifest");

        Assert.Equal("KSArmory", LoaderCrashReport.LikelyLoadingMod(output, [Code("ModMenu"), Code("MeasureTools"), Code("KSArmory")]));
    }

    [Fact]
    public void LikelyLoadingMod_DisabledAndAssetModsBefore_AreSkipped()
    {
        var output = Output(
            "StarMap - Loaded mod: ModMenu from manifest",
            "StarMap - Not loading mod: OldTools because it is disabled in manifest");

        Assert.Equal("KSArmory", LoaderCrashReport.LikelyLoadingMod(output, [new("Core", IsCodeMod: false), Code("ModMenu"), Code("OldTools"), new("Textures", IsCodeMod: false), Code("KSArmory")]));
    }

    [Fact]
    public void LikelyLoadingMod_DelayedModWhoseDependencyJustLoaded_IsThatMod()
    {
        var output = Output(
            "StarMap - Delaying load of mod: KSArmory due to missing dependencies: ModMenu",
            "StarMap - Loaded mod: ModMenu from manifest");

        Assert.Equal("KSArmory", LoaderCrashReport.LikelyLoadingMod(output, [Code("KSArmory"), Code("ModMenu"), Code("MeasureTools")]));
    }

    [Fact]
    public void LikelyLoadingMod_DelayedModWaitingOnAModNotYetLoaded_IsTheNextCodeMod()
    {
        var output = Output("StarMap - Delaying load of mod: KSArmory due to missing dependencies: ModMenu");

        Assert.Equal("MeasureTools", LoaderCrashReport.LikelyLoadingMod(output, [Code("KSArmory"), Code("MeasureTools"), Code("ModMenu")]));
    }

    [Fact]
    public void LikelyLoadingMod_DependencyInAnotherCase_DoesNotLoadTheDelayedMod()
    {
        var output = Output(
            "StarMap - Delaying load of mod: KSArmory due to missing dependencies: modmenu",
            "StarMap - Loaded mod: ModMenu from manifest");

        Assert.Equal("MeasureTools", LoaderCrashReport.LikelyLoadingMod(output, [Code("KSArmory"), Code("ModMenu"), Code("MeasureTools")]));
    }

    [Fact]
    public void LikelyLoadingMod_EveryModReported_IsTheWaitingModWhoseMissingDependenciesAreOptional()
    {
        var output = Output(
            "StarMap - Delaying load of mod: KSArmory due to missing dependencies: ModMenu, Extras",
            "StarMap - Delaying load of mod: ModMenu due to missing dependencies: ModLib",
            "StarMap - Loaded mod: ModLib from manifest",
            "StarMap - Loaded mod: ModMenu after loading ModLib");
        LoadOrderMod[] mods = [Code("KSArmory") with { OptionalDependencies = ["ModMenu", "Extras"] }, Code("ModMenu"), Code("ModLib")];

        Assert.Equal("KSArmory", LoaderCrashReport.LikelyLoadingMod(output, mods));
    }

    [Fact]
    public void LikelyLoadingMod_EveryModReportedAndTheWaitingModMissesARequiredDependency_IsNull()
    {
        var output = Output(
            "StarMap - Delaying load of mod: KSArmory due to missing dependencies: ModMenu",
            "StarMap - Delaying load of mod: ModMenu due to missing dependencies: ModLib",
            "StarMap - Loaded mod: ModLib from manifest",
            "StarMap - Loaded mod: ModMenu after loading ModLib");

        Assert.Null(LoaderCrashReport.LikelyLoadingMod(output, [Code("KSArmory"), Code("ModMenu"), Code("ModLib")]));
    }

    [Fact]
    public void LikelyLoadingMod_DependentModThatFailedToLoad_IsSkipped()
    {
        var output = Output(
            "StarMap - Delaying load of mod: ModMenu due to missing dependencies: ModLib",
            "StarMap - Loaded mod: ModLib from manifest",
            "StarMap - Failed to load mod: ModMenu after loading ModLib");

        Assert.Equal("KSArmory", LoaderCrashReport.LikelyLoadingMod(output, [Code("ModMenu"), Code("ModLib"), Code("KSArmory")]));
    }

    [Fact]
    public void LikelyLoadingMod_InvalidModDefinitionAndTheCrashInTryCreateMod_IsThatMod()
    {
        string[] output =
        [
            @"StarMap - Using Instance Path: C:\Instances\Main",
            "StarMap - Loaded mod: ModMenu from manifest",
            "Unhandled exception. Tomlet.Exceptions.TomlException: The mod.toml is not valid TOML.",
            "   at StarMap.Core.ModRepository.RuntimeMod.TryCreateMod(KSA.ModEntry, System.Runtime.Loader.AssemblyLoadContext, StarMap.Core.ModRepository.RuntimeMod ByRef)",
        ];
        LoadOrderMod[] mods = [Code("ModMenu"), new("Broken", IsCodeMod: false) { HasInvalidDefinition = true }, Code("KSArmory")];

        Assert.True(LoaderCrashReport.StoppedWhileLoadingMods(output));
        Assert.Equal("Broken", LoaderCrashReport.LikelyLoadingMod(output, mods));
    }

    [Fact]
    public void LikelyLoadingMod_InvalidModDefinitionAndTheCrashInInitializeMod_IsSkipped()
    {
        var output = Output("StarMap - Loaded mod: ModMenu from manifest");
        LoadOrderMod[] mods = [Code("ModMenu"), new("Broken", IsCodeMod: false) { HasInvalidDefinition = true }, Code("KSArmory")];

        Assert.Equal("KSArmory", LoaderCrashReport.LikelyLoadingMod(output, mods));
    }

    [Fact]
    public void LikelyLoadingMod_CrashWhileTheGamePreparesTheManifest_IsNotModLoading()
    {
        string[] output =
        [
            @"StarMap - Using Instance Path: C:\Instances\Main",
            "Unhandled exception. System.IO.IOException: The process cannot access the file 'manifest.toml' because it is being used by another process.",
            "   at System.IO.File.ReadAllText(System.String)",
            "   at KSA.ModLibrary.PrepareManifest()",
            "   at StarMap.Core.ModRepository.ModLoader.PrepareMods()",
            "   at StarMap.Core.ModRepository.ModLoader.Init()",
        ];

        Assert.False(LoaderCrashReport.StoppedWhileLoadingMods(output));
        Assert.Null(LoaderCrashReport.LikelyLoadingMod(output, [Code("ModMenu")]));
    }

    [Fact]
    public void LikelyLoadingMod_EveryModReportedAndNoneWaiting_IsNull()
    {
        var output = Output(
            "StarMap - Loaded mod: ModMenu from manifest",
            "StarMap - Failed to initialize mod: KSArmory from manifest");

        Assert.Null(LoaderCrashReport.LikelyLoadingMod(output, [Code("ModMenu"), Code("KSArmory")]));
    }

    [Fact]
    public void LikelyLoadingMod_IdsInAnotherCase_MatchAndKeepTheManifestSpelling()
    {
        var output = Output("StarMap - Loaded mod: modmenu from manifest");

        Assert.Equal("KSArmory", LoaderCrashReport.LikelyLoadingMod(output, [Code("ModMenu"), Code("KSArmory")]));
    }

    [Fact]
    public void LikelyLoadingMod_NoFrameOfTheModLoading_IsNull()
    {
        string[] output =
        [
            @"StarMap - Using Instance Path: C:\Instances\Main",
            "StarMap - Loaded mod: ModMenu from manifest",
            "Unhandled exception. System.NullReferenceException: Object reference not set to an instance of an object.",
            "   at KSA.Program.Main(String[] args)",
        ];

        Assert.False(LoaderCrashReport.StoppedWhileLoadingMods(output));
        Assert.Null(LoaderCrashReport.LikelyLoadingMod(output, [Code("ModMenu"), Code("KSArmory")]));
    }

    [Fact]
    public void LikelyLoadingMod_OutputWithoutTheInstancePath_IsNull()
    {
        string[] output = ["StarMap - Loaded mod: ModMenu from manifest", .. ClrCrash];

        Assert.True(LoaderCrashReport.StoppedWhileLoadingMods(output));
        Assert.Null(LoaderCrashReport.LikelyLoadingMod(output, [Code("ModMenu"), Code("KSArmory")]));
    }

    [Fact]
    public void LikelyLoadingMod_NoEnabledMods_IsNull()
    {
        Assert.Null(LoaderCrashReport.LikelyLoadingMod(Output(), []));
    }
}
