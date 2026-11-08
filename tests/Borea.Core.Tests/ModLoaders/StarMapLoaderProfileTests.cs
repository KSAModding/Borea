using Borea.Core.Launch;
using Borea.Core.ModLoaders;

namespace Borea.Core.Tests.ModLoaders;

public sealed class StarMapLoaderProfileTests
{
    private static readonly StarMapLoaderProfile Profile = new();

    [Theory]
    [InlineData("StarMap")]
    [InlineData("starmap")]
    [InlineData("STARMAP")]
    public void Known_PicksTheStarMapProfileByIdInAnyCase(string loaderId)
    {
        Assert.IsType<StarMapLoaderProfile>(LoaderProfiles.Known.For(loaderId));
    }

    [Theory]
    [InlineData("Oxidizer")]
    [InlineData("")]
    [InlineData(null)]
    public void Known_GivesOtherLoadersTheNeutralProfile(string? loaderId)
    {
        Assert.Same(NeutralLoaderProfile.Instance, LoaderProfiles.Known.For(loaderId));
    }

    [Fact]
    public void Known_FindsTheProfileByItsModTomlTable()
    {
        Assert.IsType<StarMapLoaderProfile>(LoaderProfiles.Known.ForModToml(name => name == "StarMap"));
        Assert.Null(LoaderProfiles.Known.ForModToml(name => name == "Oxidizer"));
        Assert.IsType<StarMapLoaderProfile>(LoaderProfiles.Known.ForModWithoutTable());
    }

    [Fact]
    public void NeutralProfile_BlamesNoModForStarMapOutput()
    {
        var neutral = NeutralLoaderProfile.Instance;
        var output = Output("StarMap - Loaded mod: ModMenu from manifest");

        Assert.False(neutral.FoundNoGame(["StarMap - Could not find KSA.dll."]));
        Assert.False(neutral.StoppedWhileLoadingMods(output));
        Assert.False(neutral.StoppedWhileLoaderStarted(LoaderStartFrames));
        Assert.Null(neutral.LikelyLoadingMod(output, [Code("ModMenu"), Code("KSArmory")], isWholeOutput: true));
        Assert.Equal("KSArmory", Profile.LikelyLoadingMod(output, [Code("ModMenu"), Code("KSArmory")]));
    }

    [Theory]
    [InlineData("StarMap - Could not find KSA.dll. Make sure the folder or file path is correct:", true)]
    [InlineData("Could not load file or assembly 'KSA, Version=2026.10.25.5707'.", false)]
    [InlineData("StarMap - The 'GameLocation' property in StarMapConfig.json is either empty or points to a non-existing file.", false)]
    public void FoundNoGame_TellsAMissingKsaDllFromOtherErrors(string line, bool expected)
    {
        Assert.Equal(expected, Profile.FoundNoGame(["StarMap - Using Instance Path: Main", line, "/home/player/Documents/ahwoo/KSA.dll"]));
    }

    private static readonly string[] LoaderStartFrames =
    [
        "   at System.Reflection.RuntimeModule.GetDefinedTypes()",
        "   at System.Reflection.RuntimeModule.GetTypes()",
        "   at StarMap.GameSurveyer.TryLoadCoreAndGame()",
        "   at StarMap.Program.Main(String[] args)",
    ];

    public static TheoryData<string[], bool> LoaderStartOutputs => new()
    {
        { ["Could not load file or assembly 'Brutal.Core.Numerics, Version=2026.9.0.0'.", .. LoaderStartFrames], true },
        { ["   at StarMap.Core.ModRepository.ModLoader..ctor(AssemblyLoadContext coreAssemblyLoadContext)", "   at StarMap.Core.StarMapCore..ctor(AssemblyLoadContext coreAssemblyLoadContext)", .. LoaderStartFrames], true },
        { ["   at StarMap.Core.ModRepository.RuntimeMod.InitializeMod(ModRegistry modRegistry)", "   at StarMap.Core.ModRepository.ModLoader.PrepareMods()", "   at StarMap.Core.StarMapCore.Init()", .. LoaderStartFrames], false },
        { ["   at StarMap.Core.ModRepository.ModsLoader.PrepareMods()", .. LoaderStartFrames], false },
        { ["Could not load file or assembly 'Brutal.Core.Numerics, Version=2026.9.0.0'.", "File name: 'Brutal.Core.Numerics, Version=2026.9.0.0'"], false },
        { ["   at KSA.Program.Main(String[] args)", "   at StarMap.GameSurveyer.RunGame()", "   at StarMap.Program.Main(String[] args)"], false },
        { StarMap050Crash, false },
    };

    [Theory]
    [MemberData(nameof(LoaderStartOutputs))]
    public void StoppedWhileLoaderStarted_NeedsTheStartFrameAndNoModLoadingFrame(string[] output, bool expected)
    {
        Assert.Equal(expected, Profile.StoppedWhileLoaderStarted(output));
    }

    [Fact]
    public void StoppedWhileLoadingMods_FrameOfTheRenamedModsLoader_Counts()
    {
        Assert.True(Profile.StoppedWhileLoadingMods(["   at StarMap.Core.ModRepository.ModsLoader.PrepareMods()"]));
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

        Assert.Equal("KSArmory", Profile.LikelyLoadingMod(output, [Code("ModMenu"), Code("MeasureTools"), Code("KSArmory")]));
    }

    [Fact]
    public void LikelyLoadingMod_DisabledAndAssetModsBefore_AreSkipped()
    {
        var output = Output(
            "StarMap - Loaded mod: ModMenu from manifest",
            "StarMap - Not loading mod: OldTools because it is disabled in manifest");

        Assert.Equal("KSArmory", Profile.LikelyLoadingMod(output, [new("Core", IsCodeMod: false), Code("ModMenu"), Code("OldTools"), new("Textures", IsCodeMod: false), Code("KSArmory")]));
    }

    [Fact]
    public void LikelyLoadingMod_DelayedModWhoseDependencyJustLoaded_IsThatMod()
    {
        var output = Output(
            "StarMap - Delaying load of mod: KSArmory due to missing dependencies: ModMenu",
            "StarMap - Loaded mod: ModMenu from manifest");

        Assert.Equal("KSArmory", Profile.LikelyLoadingMod(output, [Code("KSArmory"), Code("ModMenu"), Code("MeasureTools")]));
    }

    [Fact]
    public void LikelyLoadingMod_DelayedModWaitingOnAModNotYetLoaded_IsTheNextCodeMod()
    {
        var output = Output("StarMap - Delaying load of mod: KSArmory due to missing dependencies: ModMenu");

        Assert.Equal("MeasureTools", Profile.LikelyLoadingMod(output, [Code("KSArmory"), Code("MeasureTools"), Code("ModMenu")]));
    }

    [Fact]
    public void LikelyLoadingMod_DependencyInAnotherCase_DoesNotLoadTheDelayedMod()
    {
        var output = Output(
            "StarMap - Delaying load of mod: KSArmory due to missing dependencies: modmenu",
            "StarMap - Loaded mod: ModMenu from manifest");

        Assert.Equal("MeasureTools", Profile.LikelyLoadingMod(output, [Code("KSArmory"), Code("ModMenu"), Code("MeasureTools")]));
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

        Assert.Equal("KSArmory", Profile.LikelyLoadingMod(output, mods));
    }

    [Fact]
    public void LikelyLoadingMod_EveryModReportedAndTheWaitingModMissesARequiredDependency_IsNull()
    {
        var output = Output(
            "StarMap - Delaying load of mod: KSArmory due to missing dependencies: ModMenu",
            "StarMap - Delaying load of mod: ModMenu due to missing dependencies: ModLib",
            "StarMap - Loaded mod: ModLib from manifest",
            "StarMap - Loaded mod: ModMenu after loading ModLib");

        Assert.Null(Profile.LikelyLoadingMod(output, [Code("KSArmory"), Code("ModMenu"), Code("ModLib")]));
    }

    [Fact]
    public void LikelyLoadingMod_DependentModThatFailedToLoad_IsSkipped()
    {
        var output = Output(
            "StarMap - Delaying load of mod: ModMenu due to missing dependencies: ModLib",
            "StarMap - Loaded mod: ModLib from manifest",
            "StarMap - Failed to load mod: ModMenu after loading ModLib");

        Assert.Equal("KSArmory", Profile.LikelyLoadingMod(output, [Code("ModMenu"), Code("ModLib"), Code("KSArmory")]));
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

        Assert.True(Profile.StoppedWhileLoadingMods(output));
        Assert.Equal("Broken", Profile.LikelyLoadingMod(output, mods));
    }

    [Fact]
    public void LikelyLoadingMod_InvalidModDefinitionAndTheCrashInInitializeMod_IsSkipped()
    {
        var output = Output("StarMap - Loaded mod: ModMenu from manifest");
        LoadOrderMod[] mods = [Code("ModMenu"), new("Broken", IsCodeMod: false) { HasInvalidDefinition = true }, Code("KSArmory")];

        Assert.Equal("KSArmory", Profile.LikelyLoadingMod(output, mods));
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

        Assert.False(Profile.StoppedWhileLoadingMods(output));
        Assert.Null(Profile.LikelyLoadingMod(output, [Code("ModMenu")]));
    }

    [Fact]
    public void LikelyLoadingMod_EveryModReportedAndNoneWaiting_IsNull()
    {
        var output = Output(
            "StarMap - Loaded mod: ModMenu from manifest",
            "StarMap - Failed to initialize mod: KSArmory from manifest");

        Assert.Null(Profile.LikelyLoadingMod(output, [Code("ModMenu"), Code("KSArmory")]));
    }

    [Fact]
    public void LikelyLoadingMod_IdsInAnotherCase_MatchAndKeepTheManifestSpelling()
    {
        var output = Output("StarMap - Loaded mod: modmenu from manifest");

        Assert.Equal("KSArmory", Profile.LikelyLoadingMod(output, [Code("ModMenu"), Code("KSArmory")]));
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

        Assert.False(Profile.StoppedWhileLoadingMods(output));
        Assert.Null(Profile.LikelyLoadingMod(output, [Code("ModMenu"), Code("KSArmory")]));
    }

    [Fact]
    public void LikelyLoadingMod_OutputWithoutTheInstancePath_IsNull()
    {
        string[] output = ["StarMap - Loaded mod: ModMenu from manifest", .. ClrCrash];

        Assert.True(Profile.StoppedWhileLoadingMods(output));
        Assert.Null(Profile.LikelyLoadingMod(output, [Code("ModMenu"), Code("KSArmory")]));
    }

    // StarMap 0.5 loads the mods in the constructor of StarMapCore, which GameSurveyer.TryLoadCoreAndGame calls through Activator.CreateInstance
    private static readonly string[] StarMap050Crash =
    [
        "Unhandled exception. System.Reflection.TargetInvocationException: Exception has been thrown by the target of an invocation.",
        " ---> System.Reflection.ReflectionTypeLoadException: Unable to load one or more of the requested types.",
        "Could not load file or assembly 'Brutal.Core.Numerics, Version=2026.9.0.0, Culture=neutral, PublicKeyToken=null'. The system cannot find the file specified.",
        "   at System.Reflection.RuntimeModule.GetTypes(RuntimeModule module)",
        "   at System.Reflection.RuntimeModule.GetTypes()",
        "   at StarMap.Core.ModRepository.RuntimeMod.InitializeMod(ModRegistry modRegistry, IPatchManager patchManager, IServiceProviderService serviceProviderService)",
        "   at StarMap.Core.ModRepository.ModsLoader.PrepareMods()",
        "   at StarMap.Core.ModRepository.ModsLoader.Init()",
        "   at StarMap.Core.Services.CoreService.Init()",
        "   at StarMap.Core.StarMapCore..ctor(AssemblyLoadContext coreAssemblyLoadContext)",
        "   at System.RuntimeMethodHandle.InvokeMethod(Object target, Void** arguments, Signature sig, Boolean isConstructor)",
        "   --- End of inner exception stack trace ---",
        "   at System.Reflection.RuntimeConstructorInfo.Invoke(BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)",
        "   at System.RuntimeType.CreateInstanceImpl(BindingFlags bindingAttr, Binder binder, Object[] args, CultureInfo culture)",
        "   at System.Activator.CreateInstance(Type type, Object[] args)",
        "   at StarMap.GameSurveyer.TryLoadCoreAndGame()",
        "   at StarMap.Program.Main(String[] args)",
        "   at StarMap.Program.<Main>(String[] args)",
    ];

    private static readonly string[] StarMap050Reports =
    [
        "StarMap info: 'StarMap - Delaying load of mod: KSArmory due to missing dependencies: ModMenu'",
        "StarMap info: 'StarMap - Loaded mod: ModMenu from manifest'",
    ];

    public static TheoryData<string[], bool, string?> StarMap050Outputs => new()
    {
        { [.. StarMap050Reports, .. StarMap050Crash], true, "KSArmory" },
        { [.. StarMap050Reports, .. StarMap050Crash], false, null },
        { StarMap050Crash, true, null },
    };

    [Theory]
    [MemberData(nameof(StarMap050Outputs))]
    public void LikelyLoadingMod_StarMap050Output_NeedsTheWholeOutputWithReports(string[] output, bool isWholeOutput, string? expected)
    {
        Assert.Equal(expected, Profile.LikelyLoadingMod(output, [Code("KSArmory"), Code("ModMenu"), Code("MeasureTools")], isWholeOutput));
    }

    [Fact]
    public void LikelyLoadingMod_NoEnabledMods_IsNull()
    {
        Assert.Null(Profile.LikelyLoadingMod(Output(), []));
    }
}
