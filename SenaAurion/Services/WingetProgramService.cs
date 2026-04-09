using System.Diagnostics;

namespace SenaAurion.Services;

/// <summary>Instalación vía winget.exe con detección de ruta (App Execution Alias / WindowsApps).</summary>
public sealed class WingetProgramService
{
    private static readonly string[] WingetCandidatePaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\winget.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"WindowsApps\Microsoft.DesktopAppInstaller_8wekyb3d8bbwe\winget.exe"),
    ];

    public Task<string> InstallAsync(string packageId, CancellationToken token = default) =>
        RunWingetAsync(["install", "--id", packageId, "--exact", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"], packageId, token);

    public Task<string> UpgradeAsync(string packageId, CancellationToken token = default) =>
        RunWingetAsync(["upgrade", "--id", packageId, "--exact", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"], packageId, token);

    public Task<string> UninstallAsync(string packageId, CancellationToken token = default) =>
        RunWingetAsync(["uninstall", "--id", packageId, "--exact", "--disable-interactivity"], packageId, token);

    /// <summary>Comprueba si winget responde (útil tras instalar App Installer).</summary>
    public static Task<string> ProbeAsync(CancellationToken token = default) =>
        RunWingetRawAsync(["--version"], token);

    private static async Task<string> RunWingetAsync(string[] args, string packageId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return "El packageId es inválido.";

        var winget = ResolveWingetPath();
        if (winget is null)
        {
            return "winget no encontrado. Instala «App Installer» desde Microsoft Store o activa el alias de ejecución de aplicaciones para winget.";
        }

        try
        {
            using var process = new Process { StartInfo = CreateStartInfo(winget, args) };

            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(token);
            var errorTask = process.StandardError.ReadToEndAsync(token);

            await Task.WhenAll(
                process.WaitForExitAsync(token),
                outputTask,
                errorTask
            ).ConfigureAwait(false);

            var output = (await outputTask.ConfigureAwait(false)).Trim();
            var error = (await errorTask.ConfigureAwait(false)).Trim();

            var detail = string.IsNullOrWhiteSpace(error) ? output : error;
            var summary = TakeRelevantLines(detail, maxLines: 6);

            return process.ExitCode == 0
                ? $"Correcto: {summary}"
                : $"Error ({process.ExitCode}): {summary}";
        }
        catch (Exception ex)
        {
            return $"Error al ejecutar winget: {ex.Message}";
        }
    }

    private static async Task<string> RunWingetRawAsync(string[] args, CancellationToken token)
    {
        var winget = ResolveWingetPath();
        if (winget is null)
            return "winget no encontrado.";

        try
        {
            using var process = new Process { StartInfo = CreateStartInfo(winget, args) };
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(token);
            var errorTask = process.StandardError.ReadToEndAsync(token);
            await Task.WhenAll(process.WaitForExitAsync(token), outputTask, errorTask).ConfigureAwait(false);
            var text = (await outputTask.ConfigureAwait(false)).Trim();
            if (string.IsNullOrWhiteSpace(text))
                text = (await errorTask.ConfigureAwait(false)).Trim();
            return process.ExitCode == 0 ? text : $"Código {process.ExitCode}: {text}";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static ProcessStartInfo CreateStartInfo(string wingetPath, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = wingetPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        return psi;
    }

    private static string? ResolveWingetPath()
    {
        foreach (var p in WingetCandidatePaths)
        {
            if (File.Exists(p))
                return p;
        }

        try
        {
            using var where = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = "winget",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };
            where.Start();
            var line = where.StandardOutput.ReadLine();
            where.WaitForExit(10_000);
            if (!string.IsNullOrWhiteSpace(line) && File.Exists(line.Trim()))
                return line.Trim();
        }
        catch
        {
            // ignorar
        }

        return null;
    }

    private static string TakeRelevantLines(string text, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "Operación completada.";

        var lines = text.Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries);
        var picked = new List<string>();
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.Length == 0) continue;
            picked.Add(t);
            if (picked.Count >= maxLines) break;
        }

        return picked.Count == 0 ? text.Trim() : string.Join(" · ", picked);
    }
}
