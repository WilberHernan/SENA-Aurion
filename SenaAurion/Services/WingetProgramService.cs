using System.Diagnostics;

namespace SenaAurion.Services;

public sealed class WingetProgramService
{
    public Task<string> InstallAsync(string packageId, CancellationToken token = default) =>
        RunWingetAsync($"install --id {packageId} --exact --accept-package-agreements --accept-source-agreements --silent", packageId, token);

    public Task<string> UpgradeAsync(string packageId, CancellationToken token = default) =>
        RunWingetAsync($"upgrade --id {packageId} --exact --accept-package-agreements --accept-source-agreements --silent", packageId, token);

    public Task<string> UninstallAsync(string packageId, CancellationToken token = default) =>
        RunWingetAsync($"uninstall --id {packageId} --exact --silent", packageId, token);

    private static async Task<string> RunWingetAsync(string args, string packageId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return "El packageId es inválido.";

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"winget {args}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var process = new Process { StartInfo = startInfo };

            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(token);
            var errorTask = process.StandardError.ReadToEndAsync(token);

            await Task.WhenAll(
                process.WaitForExitAsync(token),
                outputTask,
                errorTask
            );

            var output = (await outputTask).Trim();
            var error = (await errorTask).Trim();

            var detail = string.IsNullOrWhiteSpace(error) ? output : error;

            return process.ExitCode == 0
                ? $"Correcto: {TakeFirstLine(detail)}"
                : $"Error ({process.ExitCode}): {TakeFirstLine(detail)}";
        }
        catch (Exception ex)
        {
            return $"Error al ejecutar winget: {ex.Message}";
        }
    }

    private static string TakeFirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "Operacion completada.";

        using var reader = new StringReader(text);
        return reader.ReadLine() ?? "Operacion completada.";
    }
}