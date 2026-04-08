using Microsoft.Win32;
using SenaAurion.Models;

namespace SenaAurion.Services;

public sealed class InstalledProgramsService
{
    public IReadOnlyList<InstalledProgramInfo> GetInstalledPrograms()
    {
        var results = new List<InstalledProgramInfo>();

        // HKLM 64-bit + 32-bit view
        ReadUninstallKey(results, RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
        ReadUninstallKey(results, RegistryHive.LocalMachine, RegistryView.Registry64, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");

        // HKCU per-user
        ReadUninstallKey(results, RegistryHive.CurrentUser, RegistryView.Default, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");

        // Deduplicate by name+version+publisher
        return results
            .Where(r => !string.IsNullOrWhiteSpace(r.DisplayName))
            .GroupBy(r => $"{r.DisplayName}||{r.DisplayVersion}||{r.Publisher}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
    }

    private static void ReadUninstallKey(
        List<InstalledProgramInfo> results,
        RegistryHive hive,
        RegistryView view,
        string subKeyPath)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(subKeyPath, writable: false);
            if (uninstall is null) return;

            foreach (var name in uninstall.GetSubKeyNames())
            {
                try
                {
                    using var appKey = uninstall.OpenSubKey(name, writable: false);
                    if (appKey is null) continue;

                    var displayName = (appKey.GetValue("DisplayName") as string) ?? "";
                    if (string.IsNullOrWhiteSpace(displayName)) continue;

                    // Filter out a chunk of noise
                    var systemComponent = ToDword(appKey.GetValue("SystemComponent")) == 1;
                    var parentKeyName = (appKey.GetValue("ParentKeyName") as string) ?? "";
                    if (!string.IsNullOrWhiteSpace(parentKeyName)) continue;

                    var releaseType = (appKey.GetValue("ReleaseType") as string) ?? "";
                    if (!string.IsNullOrWhiteSpace(releaseType) && releaseType.Contains("Update", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var uninstallString = (appKey.GetValue("UninstallString") as string) ?? "";
                    var quietUninstallString = (appKey.GetValue("QuietUninstallString") as string) ?? "";
                    var displayIcon = (appKey.GetValue("DisplayIcon") as string) ?? "";

                    var publisher = (appKey.GetValue("Publisher") as string) ?? "";
                    var version = (appKey.GetValue("DisplayVersion") as string) ?? "";
                    var installLocation = (appKey.GetValue("InstallLocation") as string) ?? "";
                    var windowsInstaller = ToDword(appKey.GetValue("WindowsInstaller")) == 1;

                    var regPath = $"{hive}\\{subKeyPath}\\{name}";

                    results.Add(new InstalledProgramInfo
                    {
                        DisplayName = displayName,
                        DisplayVersion = version,
                        Publisher = publisher,
                        UninstallString = uninstallString,
                        QuietUninstallString = quietUninstallString,
                        InstallLocation = installLocation,
                        DisplayIcon = displayIcon,
                        RegistryKeyPath = regPath,
                        IsSystemComponent = systemComponent,
                        IsWindowsInstaller = windowsInstaller
                    });
                }
                catch
                {
                    // ignore individual entries
                }
            }
        }
        catch
        {
        }
    }

    private static int ToDword(object? v)
    {
        return v switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var i) => i,
            _ => 0
        };
    }
}

