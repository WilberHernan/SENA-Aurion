using System.Diagnostics;
using System.Text;

namespace SenaAurion.Services;

public sealed class WingetProgramService
{
    public Task<string> InstallAsync(string packageId, CancellationToken token = default) =>
        RunWingetAsync($"install --id {packageId} --exact --accept-package-agreements --accept-source-agreements --silent", token);

    public Task<string> UpgradeAsync(string packageId, CancellationToken token = default) =>
        RunWingetAsync($"upgrade --id {packageId} --exact --accept-package-agreements --accept-source-agreements --silent", token);

    public Task<string> UninstallAsync(string packageId, CancellationToken token = default) =>
        RunWingetAsync($"uninstall --id {packageId} --exact --silent", token);

    private static async Task<string> RunWingetAsync(string args, CancellationToken token)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "winget",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return "No se pudo iniciar winget.";
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(token);
            var errorTask = process.StandardError.ReadToEndAsync(token);

            await process.WaitForExitAsync(token).ConfigureAwait(false);

            var output = (await outputTask.ConfigureAwait(false)).Trim();
            var error = (await errorTask.ConfigureAwait(false)).Trim();
            var detail = string.IsNullOrWhiteSpace(error) ? output : error;

            if (process.ExitCode == 0)
            {
                return $"Correcto: {TakeFirstLine(detail)}";
            }

            return $"Error ({process.ExitCode}): {TakeFirstLine(detail)}";
        }
        catch (Exception ex)
        {
            return $"Error al ejecutar winget: {ex.Message}";
        }
    }

    private static string TakeFirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Operacion completada.";
        }

        using var reader = new StringReader(text);
        return reader.ReadLine() ?? "Operacion completada.";
    }
}
