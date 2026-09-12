namespace Borea.Core.Mods;

public interface IModReplacer
{
    Task<ModReplacementResult> ReplaceAsync(
        Guid instanceId,
        InstalledMod expectedCurrent,
        ModVersionMetadata replacement,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record ModReplacementResult(
    InstalledMod Previous,
    InstalledMod Replacement,
    DownloadResult Download,
    string? RetainedRecoveryDirectory);

public sealed class ModReplacementRecoveryException : Exception
{
    public Exception OperationError { get; }

    public Exception RecoveryError { get; }

    public string RecoveryDirectory { get; }

    public ModReplacementRecoveryException(
        Exception operationError,
        Exception recoveryError,
        string recoveryDirectory)
        : base("The mod replacement failed, and Borea could not restore the previous installation.", operationError)
    {
        OperationError = operationError ?? throw new ArgumentNullException(nameof(operationError));
        RecoveryError = recoveryError ?? throw new ArgumentNullException(nameof(recoveryError));
        RecoveryDirectory = string.IsNullOrWhiteSpace(recoveryDirectory)
            ? throw new ArgumentException("Recovery directory cannot be null or whitespace.", nameof(recoveryDirectory))
            : recoveryDirectory;
    }
}
