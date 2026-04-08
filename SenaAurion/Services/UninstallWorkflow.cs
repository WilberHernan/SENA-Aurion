using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;
using SenaAurion.Models;

namespace SenaAurion.Services;

public static class UninstallWorkflow
{
    public static async Task<string> RunOfficialUninstallAsync(InstalledProgramInfo program, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var cmd = string.IsNullOrWhiteSpace(program.UninstallString)
            ? program.QuietUninstallString
            : program.UninstallString;

        if (string.IsNullOrWhiteSpace(cmd))
            return "Sin desinstalador registrado (omitido)";

        // Many entries are like: MsiExec.exe /I{GUID}  or /X{GUID}
        // We run the registered command as-is (official uninstaller).
        try
        {
            var psi = BuildShellExecute(cmd);
            _ = Process.Start(psi);
            await Task.Delay(300, token).ConfigureAwait(false);
            return "Desinstalador oficial iniciado";
        }
        catch (Exception ex)
        {
            return $"Error iniciando desinstalador: {ex.Message}";
        }
    }

    public static async Task<AdvancedUninstallScanResult> RunAdvancedAsync(InstalledProgramInfo program, CancellationToken token)
    {
        var notes = new List<string>();

        // 1) Close processes
        var closed = await Task.Run(() => TryCloseRelatedProcesses(program, notes), token).ConfigureAwait(false);
        if (closed > 0) notes.Add($"Procesos cerrados: {closed}");

        // 2) Try official uninstaller but continue regardless
        var uninstallOutcome = await RunOfficialUninstallAsync(program, token).ConfigureAwait(false);
        notes.Add(uninstallOutcome);

        // Small delay to allow uninstallers to spawn
        await Task.Delay(800, token).ConfigureAwait(false);

        // 3) Deep scan residues
        var items = await Task.Run(() => ScanResidues(program, notes, token), token).ConfigureAwait(false);

        return new AdvancedUninstallScanResult
        {
            ProgramName = program.DisplayName,
            Items = items,
            Notes = notes
        };
    }

    public static async Task<IReadOnlyList<string>> DeleteSelectedAsync(IEnumerable<ResidueItem> items, CancellationToken token)
    {
        var errors = new List<string>();
        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                switch (item.Type)
                {
                    case ResidueItemType.Folder:
                        FileSystemDeletion.TryDeleteDirectory(item.Path, errors);
                        break;
                    case ResidueItemType.File:
                        FileSystemDeletion.TryDeleteFile(item.Path, errors);
                        break;
                    case ResidueItemType.RegistryKey:
                        RegistryDeletion.TryDeleteKey(item.Path, errors);
                        break;
                    case ResidueItemType.Service:
                        ServiceDeletion.TryDeleteService(item.Path, errors);
                        break;
                    case ResidueItemType.ScheduledTask:
                        TaskDeletion.TryDeleteTask(item.Path, errors);
                        break;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{item.Type}: {item.Path} → {ex.Message}");
            }
        }
        await Task.Delay(50, token).ConfigureAwait(false);
        return errors;
    }

    private static ProcessStartInfo BuildShellExecute(string raw)
    {
        // We let ShellExecute parse quoting rules (like Control Panel uninstall entries).
        return new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + raw,
            UseShellExecute = true,
            CreateNoWindow = true
        };
    }

    private static int TryCloseRelatedProcesses(InstalledProgramInfo program, List<string> notes)
    {
        int closed = 0;
        var tokens = BuildTokens(program);
        var install = NormalizePath(program.InstallLocation);

        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var name = p.ProcessName ?? "";
                bool matchByName = tokens.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase));

                bool matchByPath = false;
                if (!string.IsNullOrWhiteSpace(install))
                {
                    try
                    {
                        var path = p.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(path))
                            matchByPath = NormalizePath(path).StartsWith(install, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        // Access denied on some processes
                    }
                }

                if (!matchByName && !matchByPath)
                    continue;

                if (p.HasExited) continue;
                p.CloseMainWindow();
                if (!p.WaitForExit(1500))
                {
                    p.Kill(entireProcessTree: true);
                }
                closed++;
            }
            catch
            {
            }
        }

        return closed;
    }

    private static List<ResidueItem> ScanResidues(InstalledProgramInfo program, List<string> notes, CancellationToken token)
    {
        var results = new List<ResidueItem>();
        var tokens = BuildTokens(program);
        var install = NormalizePath(program.InstallLocation);

        // Folders/files
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), // ProgramData
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), // Roaming
            Path.GetTempPath()
        }.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();
            FileSystemScanner.Scan(root, tokens, install, results);
        }

        // Registry keys scan (limited scope for speed/stability)
        RegistryScanner.Scan(tokens, results);

        // Services
        try
        {
            foreach (var svc in ServiceController.GetServices())
            {
                token.ThrowIfCancellationRequested();
                if (tokens.Any(t => svc.ServiceName.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                                    svc.DisplayName.Contains(t, StringComparison.OrdinalIgnoreCase)))
                {
                    results.Add(new ResidueItem { Category = ResidueCategory.Services, Type = ResidueItemType.Service, Path = svc.ServiceName });
                }
                else if (!string.IsNullOrWhiteSpace(install))
                {
                    // Check ImagePath in registry
                    using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{svc.ServiceName}");
                    var imagePath = (key?.GetValue("ImagePath") as string) ?? "";
                    if (!string.IsNullOrWhiteSpace(imagePath) && NormalizePath(imagePath).Contains(install, StringComparison.OrdinalIgnoreCase))
                        results.Add(new ResidueItem { Category = ResidueCategory.Services, Type = ResidueItemType.Service, Path = svc.ServiceName });
                }
            }
        }
        catch (Exception ex)
        {
            notes.Add($"Servicios: {ex.Message}");
        }

        // Scheduled tasks via schtasks
        try
        {
            foreach (var taskName in TaskScanner.QueryTaskNames(tokens))
            {
                results.Add(new ResidueItem { Category = ResidueCategory.ScheduledTasks, Type = ResidueItemType.ScheduledTask, Path = taskName });
            }
        }
        catch (Exception ex)
        {
            notes.Add($"Tareas: {ex.Message}");
        }

        return results
            .GroupBy(i => $"{i.Type}||{i.Path}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(i => i.Category)
            .ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizePath(string? p)
    {
        if (string.IsNullOrWhiteSpace(p)) return "";
        p = p.Trim().Trim('"');
        return p.Replace('/', '\\').TrimEnd('\\');
    }

    private static string[] BuildTokens(InstalledProgramInfo program)
    {
        var raw = new List<string>
        {
            program.DisplayName,
            program.Publisher
        };

        // Split name tokens
        var tokens = raw
            .SelectMany(s => (s ?? "").Split(new[] { ' ', '-', '_', '.', ',', '(', ')', '[', ']' }, StringSplitOptions.RemoveEmptyEntries))
            .Select(s => s.Trim())
            .Where(s => s.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return tokens;
    }
}

internal static class FileSystemScanner
{
    public static void Scan(string root, string[] tokens, string install, List<ResidueItem> results)
    {
        try
        {
            if (!Directory.Exists(root)) return;

            // Shallow scan to keep it fast/stable.
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(name)) continue;

                if (!string.IsNullOrWhiteSpace(install) && Normalize(dir).StartsWith(install, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new ResidueItem { Category = ResidueCategory.Folders, Type = ResidueItemType.Folder, Path = dir });
                    continue;
                }

                if (tokens.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase)))
                {
                    results.Add(new ResidueItem { Category = ResidueCategory.Folders, Type = ResidueItemType.Folder, Path = dir });
                }
            }
        }
        catch
        {
        }

        static string Normalize(string p) => p.Replace('/', '\\').TrimEnd('\\');
    }
}

internal static class RegistryScanner
{
    public static void Scan(string[] tokens, List<ResidueItem> results)
    {
        // Keep scan limited: Software hives only (avoid huge trees).
        ScanHive(Registry.CurrentUser, @"SOFTWARE", tokens, results);
        ScanHive(Registry.LocalMachine, @"SOFTWARE", tokens, results);
        ScanHive(Registry.ClassesRoot, @"", tokens, results);
    }

    private static void ScanHive(RegistryKey root, string subKey, string[] tokens, List<ResidueItem> results)
    {
        try
        {
            using var baseKey = string.IsNullOrWhiteSpace(subKey) ? root : root.OpenSubKey(subKey);
            if (baseKey is null) return;

            foreach (var child in baseKey.GetSubKeyNames())
            {
                if (tokens.Any(t => child.Contains(t, StringComparison.OrdinalIgnoreCase)))
                {
                    var path = root.Name + (string.IsNullOrWhiteSpace(subKey) ? "" : "\\" + subKey) + "\\" + child;
                    results.Add(new ResidueItem { Category = ResidueCategory.Registry, Type = ResidueItemType.RegistryKey, Path = path });
                }
            }
        }
        catch
        {
        }
    }
}

internal static class TaskScanner
{
    public static IEnumerable<string> QueryTaskNames(string[] tokens)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = "/Query /FO LIST",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi);
        if (p is null) yield break;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);

        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!line.StartsWith("TaskName:", StringComparison.OrdinalIgnoreCase)) continue;
            var name = line.Substring("TaskName:".Length).Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (tokens.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase)))
                yield return name;
        }
    }
}

internal static class FileSystemDeletion
{
    public static void TryDeleteFile(string path, List<string> errors)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
        catch (Exception ex)
        {
            errors.Add($"Archivo: {path} → {ex.Message}");
        }
    }

    public static void TryDeleteDirectory(string path, List<string> errors)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            SetNormalRecursive(path);
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            errors.Add($"Carpeta: {path} → {ex.Message}");
        }
    }

    private static void SetNormalRecursive(string dir)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
            }
        }
        catch { }
    }
}

internal static class RegistryDeletion
{
    public static void TryDeleteKey(string fullPath, List<string> errors)
    {
        try
        {
            // fullPath is like: HKEY_CURRENT_USER\SOFTWARE\Foo
            var (root, sub) = SplitRegistryPath(fullPath);
            if (root is null || string.IsNullOrWhiteSpace(sub)) return;
            root.DeleteSubKeyTree(sub, throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            errors.Add($"Registro: {fullPath} → {ex.Message}");
        }
    }

    private static (RegistryKey? root, string sub) SplitRegistryPath(string full)
    {
        full = full.Replace("/", "\\");
        if (full.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))
            return (Registry.CurrentUser, full["HKEY_CURRENT_USER\\".Length..]);
        if (full.StartsWith("HKEY_LOCAL_MACHINE\\", StringComparison.OrdinalIgnoreCase))
            return (Registry.LocalMachine, full["HKEY_LOCAL_MACHINE\\".Length..]);
        if (full.StartsWith("HKEY_CLASSES_ROOT\\", StringComparison.OrdinalIgnoreCase))
            return (Registry.ClassesRoot, full["HKEY_CLASSES_ROOT\\".Length..]);
        return (null, "");
    }
}

internal static class ServiceDeletion
{
    public static void TryDeleteService(string serviceName, List<string> errors)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"delete \"{serviceName}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            errors.Add($"Servicio: {serviceName} → {ex.Message}");
        }
    }
}

internal static class TaskDeletion
{
    public static void TryDeleteTask(string taskName, List<string> errors)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Delete /TN \"{taskName}\" /F",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(8000);
        }
        catch (Exception ex)
        {
            errors.Add($"Tarea: {taskName} → {ex.Message}");
        }
    }
}

