using System.CommandLine;
using System.Globalization;
using Borea.Cli.Output;
using Borea.Core.Instances;

namespace Borea.Cli.Commands;

/// <summary>
/// <c>borea instance backups</c>, <c>restore-backup</c> and <c>delete-backup</c>:
/// the saves and vehicles that Borea moved or zipped into the Backups folder.
/// </summary>
internal static class BackupCommands
{
    private const string BackupArgumentDescription = "The backup's id from 'borea instance backups', or its file name when only one backup has it.";

    public static Command BuildList(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = ArgumentRules.Text("instance", InstanceCommand.InstanceArgumentDescription);
        var json = ArgumentRules.Json();
        var list = new Command("backups", "Print the backups of the saves and vehicles of an instance, newest first.");
        list.Arguments.Add(instance);
        list.Options.Add(json);

        list.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var target = await InstanceLookup.ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance)).ConfigureAwait(false);
            var backups = await cli.GameSaveBackups.ListAsync(target.InstanceId, ct).ConfigureAwait(false);

            if (parseResult.GetValue(json))
            {
                JsonOutput.Write(output, backups.Select(BackupView.From));
                return ExitCodes.Done;
            }

            if (backups.Count == 0)
            {
                output.WriteLine($"No backups of '{target.Name}'.");
                return ExitCodes.Done;
            }

            var nameWidth = backups.Max(backup => backup.Name.Length);
            foreach (var backup in backups)
                output.WriteLine($"{Timestamp(backup.CreatedAt)}  {KindName(backup.Kind) ?? "unknown",-7}  {backup.Name.PadRight(nameWidth)}  {ReasonName(backup.Reason),-9}  {backup.Id}");

            return ExitCodes.Done;
        }));

        return list;
    }

    public static Command BuildRestore(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = ArgumentRules.Text("instance", InstanceCommand.InstanceArgumentDescription);
        var backupId = ArgumentRules.Text("backup", BackupArgumentDescription);
        var replace = new Option<bool>("--replace") { Description = "Move a save or vehicle of the same name into the backups, and restore this backup in its place." };
        var json = ArgumentRules.Json();
        var restore = new Command("restore-backup", "Put a backup back where it came from. A zip stays in the backups, a moved folder leaves them. Close the game first.");
        restore.Arguments.Add(instance);
        restore.Arguments.Add(backupId);
        restore.Options.Add(replace);
        restore.Options.Add(json);

        restore.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var target = await InstanceLookup.ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance)).ConfigureAwait(false);
            var backup = await FindAsync(cli, target, parseResult.GetRequiredValue(backupId), ct).ConfigureAwait(false);

            // the game holds a save file open only while it writes it, so a file check alone misses a running game
            if (cli.IsGameProcessRunning())
                throw new InvalidOperationException("Close the game before you restore a backup.");

            var outcome = await cli.GameSaveBackups.RestoreAsync(backup, parseResult.GetValue(replace), ct).ConfigureAwait(false);
            if (outcome == GameSaveRestoreOutcome.Exists)
                throw new InvalidOperationException($"'{target.Name}' already has the {KindName(backup.Kind)} folder '{backup.FolderName}'. Add --replace to move it into the backups and restore this backup.");

            if (parseResult.GetValue(json))
                JsonOutput.Write(output, BackupView.From(backup));
            else
                output.WriteLine($"Restored '{backup.Name}' into '{target.Name}'.");

            return ExitCodes.Done;
        }));

        return restore;
    }

    public static Command BuildDelete(Func<CancellationToken, Task<CliServices>> services)
    {
        var instance = ArgumentRules.Text("instance", InstanceCommand.InstanceArgumentDescription);
        var backupId = ArgumentRules.Text("backup", BackupArgumentDescription);
        var json = ArgumentRules.Json();
        var delete = new Command("delete-backup", "Delete a backup for good.");
        delete.Arguments.Add(instance);
        delete.Arguments.Add(backupId);
        delete.Options.Add(json);

        delete.SetAction((parseResult, cancellationToken) => CommandRunner.RunAsync(parseResult, services, cancellationToken, async (cli, output, _, ct) =>
        {
            var target = await InstanceLookup.ResolveAsync(cli.Instances, parseResult.GetRequiredValue(instance)).ConfigureAwait(false);
            var backup = await FindAsync(cli, target, parseResult.GetRequiredValue(backupId), ct).ConfigureAwait(false);
            await cli.GameSaveBackups.DeleteAsync(backup, ct).ConfigureAwait(false);

            if (parseResult.GetValue(json))
                JsonOutput.Write(output, BackupView.From(backup));
            else
                output.WriteLine($"Deleted the backup {backup.Id} of '{target.Name}'.");

            return ExitCodes.Done;
        }));

        return delete;
    }

    private static async Task<GameSaveBackup> FindAsync(CliServices cli, Instance instance, string text, CancellationToken cancellationToken)
    {
        var backups = await cli.GameSaveBackups.ListAsync(instance.InstanceId, cancellationToken).ConfigureAwait(false);
        var id = text.Replace('\\', '/');
        var byId = backups.FirstOrDefault(backup => string.Equals(backup.Id, id, StringComparison.Ordinal))
            ?? backups.FirstOrDefault(backup => string.Equals(backup.Id, id, StringComparison.OrdinalIgnoreCase));
        if (byId is not null)
            return byId;

        var named = backups.Where(backup => string.Equals(backup.Id.Split('/')[^1], id, StringComparison.OrdinalIgnoreCase)).ToList();
        return named.Count switch
        {
            1 => named[0],
            0 => throw new InvalidOperationException($"'{instance.Name}' has no backup '{text}'. Run 'borea instance backups {instance.Name}' to list them."),
            _ => throw new InvalidOperationException($"More than one backup of '{instance.Name}' is named '{text}'. Name it by id: {string.Join(", ", named.Select(backup => backup.Id))}."),
        };
    }

    private static string Timestamp(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string? KindName(GameSaveKind? kind) => kind switch
    {
        GameSaveKind.Save => "save",
        GameSaveKind.Vehicle => "vehicle",
        _ => null,
    };

    private static string ReasonName(GameSaveBackupReason reason) => reason switch
    {
        GameSaveBackupReason.BackedUp => "backed-up",
        GameSaveBackupReason.Deleted => "deleted",
        GameSaveBackupReason.Replaced => "replaced",
        _ => "unknown",
    };

    /// <summary>One entry of <c>instance backups --json</c>, and the JSON shape of <c>restore-backup</c> and <c>delete-backup</c>. Kind and folder are null when Borea cannot tell where the backup came from.</summary>
    private sealed record BackupView(string Id, string? Kind, string? Folder, string Name, DateTimeOffset CreatedAt, string Reason, bool Archive, long SizeBytes, bool CanRestore)
    {
        public static BackupView From(GameSaveBackup backup)
            => new(backup.Id, KindName(backup.Kind), backup.FolderName, backup.Name, backup.CreatedAt, ReasonName(backup.Reason), backup.IsArchive, backup.SizeBytes, backup.CanRestore);
    }
}
