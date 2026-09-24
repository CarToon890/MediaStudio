namespace MediaStudio.Models;

public enum AppUpdateCheckStatus
{
    Success,
    Failed
}

public sealed class AppReleaseInfo
{
    public required string TagName { get; init; }
    public required Version Version { get; init; }
    public required string ReleaseUrl { get; init; }
    public string PackageUrl { get; init; } = string.Empty;
    public string ChecksumUrl { get; init; } = string.Empty;
    public bool CanAutoUpdate => !string.IsNullOrWhiteSpace(PackageUrl) && !string.IsNullOrWhiteSpace(ChecksumUrl);
}

public sealed class AppUpdateCheckResult
{
    public AppUpdateCheckStatus Status { get; init; }
    public AppReleaseInfo? Release { get; init; }
    public bool IsUpdateAvailable { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;

    public static AppUpdateCheckResult Completed(AppReleaseInfo release, bool available) => new()
    {
        Status = AppUpdateCheckStatus.Success,
        Release = release,
        IsUpdateAvailable = available
    };

    public static AppUpdateCheckResult Failed(string message) => new()
    {
        Status = AppUpdateCheckStatus.Failed,
        ErrorMessage = message
    };
}

public sealed class StagedAppUpdate
{
    public required AppReleaseInfo Release { get; init; }
    public required string StagingDirectory { get; init; }
    public required string ExecutablePath { get; init; }
}

public sealed class AppUpdateProgress
{
    public required string Message { get; init; }
    public double Percentage { get; init; }
}

public sealed class UpdateLaunchResult
{
    public bool Success { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;

    public static UpdateLaunchResult Started() => new() { Success = true };
    public static UpdateLaunchResult Failed(string message) => new() { ErrorMessage = message };
}

public sealed class AppUpdateCompletion
{
    public bool Success { get; init; }
    public string Version { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string ReleaseUrl { get; init; } = string.Empty;
}
