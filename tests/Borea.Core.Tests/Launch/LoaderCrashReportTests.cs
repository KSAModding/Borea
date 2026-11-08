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

    [Theory]
    [InlineData("System.IO.FileNotFoundException: Could not load file or assembly '/Users/me/mods/Foo/Foo.dll'. The system cannot find the file specified.")]
    [InlineData(@"System.IO.FileNotFoundException: Could not load file or assembly 'C:\Games\KSA\mods\Foo\Foo.dll'. The system cannot find the file specified.")]
    [InlineData("Could not load file or assembly 'file:///C:/Games/KSA/mods/Foo/Foo.DLL'.")]
    public void AssemblyNames_NameGivenAsAPath_IsItsFileNameWithoutTheExtension(string line)
    {
        Assert.Equal(["Foo"], LoaderCrashReport.AssemblyNames([line]));
        Assert.Equal(["Foo"], LoaderCrashReport.AssemblyNames([line, "Method 'Draw' in type 'Foo.Tool' from assembly 'Foo, Version=1.0.0.0' does not have an implementation."]));
    }

    [Theory]
    [InlineData("Could not load file or assembly '*'.")]
    [InlineData("Could not load file or assembly '/Users/me/mods/Foo/F?o.dll'.")]
    [InlineData(@"Could not load file or assembly 'C:\Games\KSA\mods\*.dll'.")]
    [InlineData("Could not load file or assembly 'C:Foo.dll'.")]
    [InlineData("Could not load file or assembly '/Users/me/mods/Foo/'.")]
    [InlineData("Could not load file or assembly '..'.")]
    public void AssemblyNames_NameThatIsNoPlainFileName_IsLeftOut(string line)
    {
        Assert.Equal(["KSArmory"], LoaderCrashReport.AssemblyNames([line, "Could not load file or assembly 'KSArmory, Version=0.8.44.0'."]));
    }

    [Fact]
    public void AssemblyNames_OutputWithoutAssemblies_IsEmpty()
    {
        Assert.Empty(LoaderCrashReport.AssemblyNames(["Unhandled exception. System.NullReferenceException", ""]));
    }

    [Theory]
    [InlineData("Could not load file or assembly 'Brutal.Core.Numerics, Version=2026.9.0.0, Culture=neutral, PublicKeyToken=null'. The system cannot find the file specified.", true)]
    [InlineData("Could not load type 'KSA.Vehicle' from assembly 'KSA, Version=2026.10.19.5638'.", true)]
    [InlineData("Could not load file or assembly 'KSArmory, Version=0.8.44.0'.", false)]
    [InlineData("Unhandled exception. System.NullReferenceException", false)]
    public void NamesGameAssembly_TellsGameAssembliesFromOthers(string line, bool expected)
    {
        Assert.Equal(expected, LoaderCrashReport.NamesGameAssembly([line]));
    }

    [Theory]
    [InlineData("KSA", true)]
    [InlineData("Brutal.Core.Numerics", true)]
    [InlineData("brutal.numerics", true)]
    [InlineData("KSArmory", false)]
    [InlineData("0Harmony", false)]
    [InlineData("StarMap.Core", false)]
    public void IsGameAssembly_TakesKsaAndBrutalAssemblies(string name, bool expected)
    {
        Assert.Equal(expected, LoaderCrashReport.IsGameAssembly(name));
    }

    [Fact]
    public void MissingAssemblyNames_TakesOnlyTheAssembliesThatCouldNotLoad()
    {
        string[] output =
        [
            "Method 'DrawAxes' in type 'KSArmory.RoundFollowable' from assembly 'KSArmory, Version=0.8.44.0' does not have an implementation.",
            "System.IO.FileNotFoundException: Could not load file or assembly 'Brutal.Core.Numerics, Version=2026.9.0.0, Culture=neutral, PublicKeyToken=null'. The system cannot find the file specified.",
            "File name: 'Brutal.Core.Numerics, Version=2026.9.0.0, Culture=neutral, PublicKeyToken=null'",
            "System.IO.FileLoadException: Could not load file or assembly '0Harmony, Version=2.3.0.0'. The located assembly's manifest definition does not match the assembly reference.",
            "Could not load file or assembly 'brutal.core.numerics, Version=2026.9.0.0'.",
        ];

        Assert.Equal(["Brutal.Core.Numerics", "0Harmony"], LoaderCrashReport.MissingAssemblyNames(output));
    }
}
