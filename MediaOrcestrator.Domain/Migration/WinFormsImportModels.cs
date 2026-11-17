using LiteDB;

namespace MediaOrcestrator.Domain.Migration;

public enum WinFormsInspectionIssueCode
{
    None = 0,
    FolderNotFound = 1,
    SelfTarget = 2,
    WpfInstallation = 3,
    MissingWinFormsExecutable = 4,
    MissingSettingsFile = 5,
    UnsupportedVersion = 6,
    MissingDatabase = 7,
    PendingDatabaseRestore = 8,
    InspectionFailed = 9,
}

public enum WinFormsImportWarningKind
{
    None = 0,
    ExternalHardDiskPath = 1,
    LegacyYoutubePlaywrightAuth = 2,
    MissingExternalSessionFile = 3,
    PendingWalLog = 4,
    CustomSourceStatePath = 5,
}

public enum WinFormsImportDestinationMode
{
    None = 0,
    SeparateInstance = 1,
    ReplaceCurrent = 2,
}

public enum WinFormsImportStage
{
    None = 0,
    Validating = 1,
    WaitingForProcessExit = 2,
    StagingSnapshot = 3,
    CopyingApplication = 4,
    WritingSettings = 5,
    CreatingShortcut = 6,
    SchedulingRestart = 7,
    Completed = 8,
}

public enum WinFormsImportErrorCode
{
    None = 0,
    InvalidSource = 1,
    SourceRunning = 2,
    TargetOverlap = 3,
    TargetNotEmpty = 4,
    SourceChangedDuringCopy = 5,
    OperationFailed = 6,
}

public enum WinFormsPendingImportOutcomeStatus
{
    None = 0,
    Applied = 1,
    RolledBack = 2,
    Failed = 3,
    RollbackFailed = 4,
}

public sealed class WinFormsImportException(WinFormsImportErrorCode code, string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException)
{
    public WinFormsImportErrorCode Code { get; } = code;
}

public sealed record LegacySettingsSnapshot(
    string SourceFolder,
    IReadOnlyDictionary<string, string> RawValues,
    string PluginPath,
    string DatabasePath,
    string TempPath,
    string StatePath);

public sealed record WinFormsProcessStatus(
    bool IsBusy,
    bool IsMutexHeld,
    IReadOnlyList<int> RunningProcessIds,
    bool IsDatabaseLocked,
    bool IsWalLocked)
{
    public static WinFormsProcessStatus Free { get; } = new(false, false, [], false, false);
}

public sealed record WinFormsImportWarning(
    WinFormsImportWarningKind Kind,
    string SourceId,
    string SourceTitle,
    string Message);

public sealed record WinFormsDatabaseSummary(
    int SourcesCount,
    int MediasCount,
    int MediaLinksCount,
    int RelationsCount,
    int CoverTemplatesCount,
    bool HasWalLog,
    long DatabaseSizeBytes,
    long WalLogSizeBytes,
    IReadOnlyList<string> HardDiskPaths,
    IReadOnlyList<WinFormsImportWarning> Warnings);

public sealed record WinFormsInspectionResult(
    bool IsValid,
    string RequestedPath,
    string CanonicalPath,
    WinFormsInspectionIssueCode IssueCode,
    string? ErrorMessage,
    Version? DetectedVersion,
    LegacySettingsSnapshot? Settings,
    WinFormsProcessStatus ProcessStatus,
    WinFormsDatabaseSummary? Summary);

public sealed record WinFormsImportProgress(
    WinFormsImportStage Stage,
    string Message,
    double? Percent = null);

public sealed record WinFormsStagedSnapshot(
    string StagingDirectory,
    string DatabasePath,
    string StateDirectory,
    string TemplatesCoversDirectory,
    string SourceDatabaseSha256,
    string? SourceWalSha256,
    WinFormsDatabaseSummary Summary,
    IReadOnlyList<WinFormsImportWarning> Warnings);

public sealed record WinFormsSeparateInstanceOptions(
    string SourceFolder,
    string TargetFolder,
    bool CreateShortcut = true,
    string? ShortcutDirectory = null,
    string? ShortcutName = null,
    bool EnableAutoSync = false,
    bool LaunchAfterCreate = false,
    string? CurrentAppBaseDirectory = null,
    string? UpdateRepo = null);

public sealed record WinFormsSeparateInstanceResult(
    string TargetFolder,
    string ExecutablePath,
    string SettingsPath,
    string DatabasePath,
    string? ShortcutPath,
    WinFormsDatabaseSummary Summary,
    IReadOnlyList<WinFormsImportWarning> Warnings);

public sealed record WinFormsReplaceCurrentOptions(
    string SourceFolder,
    bool EnableAutoSync = false,
    string? TargetAppBaseDirectory = null,
    string? CurrentDatabasePath = null,
    string? CurrentStatePath = null,
    string? UpdateRepo = null,
    LiteDatabase? CurrentDatabase = null);

public sealed record WinFormsPendingImportManifest(
    string OperationId,
    string StagingDirectory,
    string SourceFolder,
    string SourceDatabaseSha256,
    string TargetDatabasePath,
    string TargetStatePath,
    string TargetTemplatesCoversPath,
    string TargetSettingsPath,
    string PreviousDatabasePath,
    string PreviousStatePath,
    string BackupsDirectory,
    WinFormsDatabaseSummary Summary,
    IReadOnlyList<WinFormsImportWarning> Warnings,
    DateTimeOffset CreatedAtUtc);

public sealed record WinFormsReplaceCurrentResult(
    string PendingManifestPath,
    string ExecutablePath,
    string RestartArguments,
    WinFormsPendingImportManifest Manifest);

public sealed record WinFormsPendingImportOutcome(
    WinFormsPendingImportOutcomeStatus Status,
    string Message,
    string? BackupDirectory = null,
    string? ErrorMessage = null,
    WinFormsDatabaseSummary? Summary = null,
    IReadOnlyList<WinFormsImportWarning>? Warnings = null);
