using System.ComponentModel;
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
        RunWingetAsync(["install", "--id", packageId, "--exact", "--source", "winget", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity", "--silent"], packageId, token);

    public Task<string> UpgradeAsync(string packageId, CancellationToken token = default) =>
        RunWingetAsync(["upgrade", "--id", packageId, "--exact", "--source", "winget", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity", "--silent"], packageId, token);

    public Task<string> UninstallAsync(string packageId, CancellationToken token = default) =>
        RunWingetAsync(["uninstall", "--id", packageId, "--exact", "--source", "winget", "--disable-interactivity", "--silent"], packageId, token);

    public static bool IsSuccessResult(string result) =>
        !string.IsNullOrWhiteSpace(result) && result.StartsWith("Correcto:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Comprueba si winget responde (útil tras instalar App Installer).</summary>
    public static Task<string> ProbeAsync(CancellationToken token = default) =>
        RunWingetRawAsync(["--version"], token);

    private static async Task<string> RunWingetAsync(string[] args, string packageId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return "El packageId es inválido.";

        var startupErrors = new List<string>();
        foreach (var candidate in ResolveWingetCandidates())
        {
            try
            {
                using var process = new Process { StartInfo = CreateStartInfo(candidate, args) };

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
            catch (Win32Exception ex)
            {
                startupErrors.Add($"{candidate}: {ex.Message}");
            }
            catch (Exception ex)
            {
                return $"Error al ejecutar winget: {ex.Message}";
            }
        }

        var startupDetail = startupErrors.Count == 0
            ? ""
            : " Detalle: " + string.Join(" | ", startupErrors.Take(2));

        return "winget no encontrado o no ejecutable. Instala/actualiza «App Installer» y activa el alias de ejecución para winget." + startupDetail;
    }

    private static async Task<string> RunWingetRawAsync(string[] args, CancellationToken token)
    {
        foreach (var candidate in ResolveWingetCandidates())
        {
            try
            {
                using var process = new Process { StartInfo = CreateStartInfo(candidate, args) };
                process.Start();
                var outputTask = process.StandardOutput.ReadToEndAsync(token);
                var errorTask = process.StandardError.ReadToEndAsync(token);
                await Task.WhenAll(process.WaitForExitAsync(token), outputTask, errorTask).ConfigureAwait(false);
                var text = (await outputTask.ConfigureAwait(false)).Trim();
                if (string.IsNullOrWhiteSpace(text))
                    text = (await errorTask.ConfigureAwait(false)).Trim();
                return process.ExitCode == 0 ? text : $"Código {process.ExitCode}: {text}";
            }
            catch (Win32Exception)
            {
                // probar siguiente candidato
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        return "winget no encontrado.";
    }

    private static ProcessStartInfo CreateStartInfo(string wingetCommand, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = wingetCommand,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        return psi;
    }

    private static IEnumerable<string> ResolveWingetCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) App execution alias (camino recomendado)
        if (seen.Add("winget"))
            yield return "winget";

        // 2) Rutas conocidas
        foreach (var p in WingetCandidatePaths)
        {
            if (File.Exists(p) && seen.Add(p))
                yield return p;
        }

        // 3) where.exe (puede devolver más de una ruta)
        var discovered = new List<string>();
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
            var output = where.StandardOutput.ReadToEnd();
            where.WaitForExit(10_000);

            foreach (var raw in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var path = raw.Trim();
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && seen.Add(path))
                    discovered.Add(path);
            }
        }
        catch
        {
            // ignorar
        }

        foreach (var path in discovered)
            yield return path;
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
