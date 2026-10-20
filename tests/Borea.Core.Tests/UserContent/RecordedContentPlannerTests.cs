using Borea.Core.Game;
using Borea.Core.Instances;
using Borea.Core.Mods;
using Borea.Core.Planning;
using Borea.Core.Tests.ModPacks;
using Borea.Core.Tests.Mods;
using Borea.Core.UserContent;
using static Borea.Core.Tests.UserContent.RecordedContentFixtures;

namespace Borea.Core.Tests.UserContent;

public sealed class RecordedContentPlannerTests
{
    private readonly Instance _instance = new("Test", InstanceSource.Custom.Value);

    public RecordedContentPlannerTests() => _instance.AddMod(TestFixtures.SampleInstalledMod("parts", "1.0.0"));

    [Fact]
    public async Task Plan_UpdatePastTheMaxOfARecordedSave_WarnsAndNamesTheSave()
    {
        var save = Record(ContentType.Save, "career", "Career", Required("parts", "1.0.0", "1.9.0"));

        var plan = await Planner([save]).PlanAsync(Request("parts", "2.0.0"));

        var warning = Assert.Single(plan.Warnings);
        Assert.Equal(PlanningMessageKind.BreaksSave, warning.Kind);
        Assert.Equal("parts", warning.ModId);
        Assert.Equal(ModVersion.Parse("2.0.0"), warning.Version);
        Assert.Equal("Career", warning.Value);
        Assert.Same(save.Metadata.Dependencies[0], warning.Dependency);
        Assert.True(plan.IsReady);
        Assert.Single(plan.Operations);
    }

    [Fact]
    public async Task Plan_UpdateBelowTheMinOfARecordedVehicle_WarnsWithTheVehicleKind()
    {
        var rover = Record(ContentType.Vehicle, "rover", "Rover", Required("parts", "1.0.0"));

        var plan = await Planner([rover]).PlanAsync(Request("parts", "0.9.0"));

        Assert.Equal(PlanningMessageKind.BreaksVehicle, Assert.Single(plan.Warnings).Kind);
    }

    [Fact]
    public async Task Plan_UpdateInsideTheBounds_AddsNoWarning()
    {
        var save = Record(ContentType.Save, "career", "Career", Required("parts", "1.0.0", "1.9.0"));

        var plan = await Planner([save]).PlanAsync(Request("parts", "1.5.0"));

        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task Plan_ModTheInstanceDidNotHave_AddsNoWarning()
    {
        var save = Record(ContentType.Save, "career", "Career", Required("engines", "1.0.0", "1.9.0"));

        var plan = await Planner([save]).PlanAsync(Request("engines", "2.0.0"));

        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task Plan_UpdateOfTheItemItself_LeavesItsOldReleaseOut()
    {
        var save = Record(ContentType.Save, "career", "Career", Required("parts", "1.0.0", "1.9.0"));
        var newer = Release(ContentType.Save, "career", "Career", [Required("parts", "2.0.0")], "2.0.0");
        var request = new InstallPlanningRequest(_instance, [new RequestedMod(newer, InstallReason.Manual)], new PackMemberRepository([], []));

        var plan = await new RecordedContentPlanner(new FixedPlanner(Update("parts", "2.0.0")), new Records([save])).PlanAsync(request);

        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task Plan_RecordFileThatCannotBeRead_AddsNoWarningAndKeepsThePlan()
    {
        var plan = await new RecordedContentPlanner(new FixedPlanner(Update("parts", "2.0.0")), new Records(null)).PlanAsync(Request("parts", "2.0.0"));

        Assert.Empty(plan.Warnings);
        Assert.Single(plan.Operations);
    }

    private RecordedContentPlanner Planner(IReadOnlyList<InstalledContentRecord> records) =>
        new(new FixedPlanner(), new Records(records));

    private InstallPlanningRequest Request(string id, string version) =>
        new(_instance, [new RequestedMod(TestFixtures.SampleVersionMetadata(id, version), InstallReason.Manual)], new PackMemberRepository([], []));

    private static PlannedInstall Update(string id, string version) =>
        new(TestFixtures.SampleVersionMetadata(id, version), InstallReason.Manual, GameCompatibility.Compatible, null);

    // Plans each requested release as one operation, or the given operation.
    private sealed class FixedPlanner(PlannedInstall? operation = null) : IInstallPlanner
    {
        public Task<InstallPlan> PlanAsync(InstallPlanningRequest request, CancellationToken cancellationToken = default)
        {
            var operations = operation is null
                ? request.Requested.Select(requested => new PlannedInstall(requested.Release, requested.Reason, GameCompatibility.Compatible, null)).ToList()
                : [operation];
            return Task.FromResult(new InstallPlan(request.Instance.InstanceId, InstallPlanningState.Capture(request.Instance), [], operations, [], [], [], []));
        }
    }

    // A null list stands for a record file that cannot be read.
    private sealed class Records(IReadOnlyList<InstalledContentRecord>? records) : IInstalledContentRecords
    {
        public Task<IReadOnlyList<InstalledContentRecord>> ListAsync(Guid instanceId, CancellationToken cancellationToken = default) =>
            records is null ? throw new InvalidDataException("broken") : Task.FromResult(records);

        public Task<InstalledContentRecord?> FindAsync(Guid instanceId, ContentType type, string folderName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddAsync(Guid instanceId, InstalledContentRecord record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReplaceAsync(Guid instanceId, string oldFolderName, InstalledContentRecord record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RemoveAsync(Guid instanceId, ContentType type, string folderName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RenameFolderAsync(Guid instanceId, ContentType type, string oldFolderName, string newFolderName, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstalledContentChanges> CompareAsync(Guid instanceId, InstalledContentRecord record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<InstalledContentRecord>> DropMissingAsync(Guid instanceId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
