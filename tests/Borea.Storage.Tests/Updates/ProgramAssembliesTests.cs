using System.Diagnostics;
using Borea.Storage.Updates;

namespace Borea.Storage.Tests.Updates;

public sealed class ProgramAssembliesTests
{
    [Fact]
    public void LoadAll_LoadsWhatTheHandoverStartsTheNewBuildWith()
    {
        var loaded = ProgramAssemblies.LoadAll(typeof(FileSelfUpdater).Assembly);

        Assert.Contains(typeof(Process).Assembly.GetName().Name!, loaded);
        Assert.Contains("Borea.Core", loaded);
        Assert.All(loaded, name => Assert.Contains(AppDomain.CurrentDomain.GetAssemblies(), assembly => assembly.GetName().Name == name));
    }
}
