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
}
