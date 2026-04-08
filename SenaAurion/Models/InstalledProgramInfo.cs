namespace SenaAurion.Models;

public sealed class InstalledProgramInfo
{
    public string DisplayName { get; init; } = "";
    public string DisplayVersion { get; init; } = "";
    public string Publisher { get; init; } = "";

    public string UninstallString { get; init; } = "";
    public string QuietUninstallString { get; init; } = "";
    public string InstallLocation { get; init; } = "";
    public string DisplayIcon { get; init; } = "";
    public string RegistryKeyPath { get; init; } = "";

    public bool IsSystemComponent { get; init; }
    public bool IsWindowsInstaller { get; init; }
}

