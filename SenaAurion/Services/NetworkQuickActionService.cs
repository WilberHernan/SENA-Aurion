using System.Diagnostics;
using SenaAurion.Models;

namespace SenaAurion.Services;

/// <summary>Acciones de red basadas en comandos del sistema (DNS, caché), alineadas con utilidades tipo WinUtil.</summary>
public sealed class NetworkQuickActionService(IOptimizationLogger log)
{
    public async Task<string> ApplyAsync(NetworkQuickActionDefinition def, CancellationToken cancellationToken = default)
    {
        await log.LogAsync("Red", $"QuickAction:{def.Id}", "Inicio", cancellationToken).ConfigureAwait(false);
        var outcome = await Task.Run(() => Execute(def.Action), cancellationToken).ConfigureAwait(false);
        await log.LogAsync("Red", $"QuickAction:{def.Id}", outcome, cancellationToken).ConfigureAwait(false);
        SystemStateMonitor.NotifyStateChanged();
        return outcome;
    }

    private static string Execute(string action)
    {
        return action.Trim().ToLowerInvariant() switch
        {
            "flushdns" => RunFlushDns(),
            "dnscloudflare" => RunPowerShellScript(DnsCloudflareScript, "DNS Cloudflare (1.1.1.1)"),
            "dnsgoogle" => RunPowerShellScript(DnsGoogleScript, "DNS Google (8.8.8.8)"),
            "dnsadguard" => RunPowerShellScript(DnsAdGuardScript, "DNS AdGuard"),
            "dnsdhcpreset" => RunPowerShellScript(DnsDhcpResetScript, "Restaurar DNS por DHCP"),
            _ => $"Acción no reconocida: {action}"
        };
    }

    private static string RunFlushDns()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "ipconfig",
                Arguments = "/flushdns",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            if (p is null) return "No se pudo iniciar ipconfig.";
            p.WaitForExit(60_000);
            var err = p.StandardError.ReadToEnd();
            return p.ExitCode == 0
                ? "Caché DNS vaciada correctamente."
                : $"ipconfig terminó con código {p.ExitCode}: {err.Trim()}";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static string RunPowerShellScript(string script, string label)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };
            p.StartInfo.ArgumentList.Add("-NoProfile");
            p.StartInfo.ArgumentList.Add("-NonInteractive");
            p.StartInfo.ArgumentList.Add("-ExecutionPolicy");
            p.StartInfo.ArgumentList.Add("Bypass");
            p.StartInfo.ArgumentList.Add("-Command");
            p.StartInfo.ArgumentList.Add(script);
            p.Start();
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(120_000);
            var tail = string.IsNullOrWhiteSpace(stderr) ? stdout.Trim() : stderr.Trim();
            if (p.ExitCode != 0)
                return $"{label}: PowerShell código {p.ExitCode}. {TakeFirstMeaningfulLine(tail)}";
            var msg = TakeFirstMeaningfulLine(tail);
            return string.IsNullOrWhiteSpace(msg) ? $"{label}: completado." : $"{label}: {msg}";
        }
        catch (Exception ex)
        {
            return $"{label}: {ex.Message}";
        }
    }

    private static string TakeFirstMeaningfulLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        foreach (var line in text.Split('\r', '\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var t = line.Trim();
            if (t.Length > 0) return t;
        }
        return text.Trim();
    }

    // Adaptadores físicos activos; requiere permisos elevados para aplicar en algunos equipos.
    private const string DnsCloudflareScript =
        "$e = $null; " +
        "try { " +
        "$a = Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $_.Virtual -eq $false }; " +
        "if (-not $a) { Write-Output 'Sin adaptador físico activo.'; exit 0 } " +
        "foreach ($x in $a) { Set-DnsClientServerAddress -InterfaceAlias $x.Name -ServerAddresses @('1.1.1.1','1.0.0.1') -ErrorAction Stop }; " +
        "Write-Output 'Aplicado en adaptadores activos.' " +
        "} catch { Write-Error $_.Exception.Message; exit 1 }";

    private const string DnsGoogleScript =
        "$e = $null; " +
        "try { " +
        "$a = Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $_.Virtual -eq $false }; " +
        "if (-not $a) { Write-Output 'Sin adaptador físico activo.'; exit 0 } " +
        "foreach ($x in $a) { Set-DnsClientServerAddress -InterfaceAlias $x.Name -ServerAddresses @('8.8.8.8','8.8.4.4') -ErrorAction Stop }; " +
        "Write-Output 'Aplicado en adaptadores activos.' " +
        "} catch { Write-Error $_.Exception.Message; exit 1 }";

    private const string DnsAdGuardScript =
        "$e = $null; " +
        "try { " +
        "$a = Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $_.Virtual -eq $false }; " +
        "if (-not $a) { Write-Output 'Sin adaptador físico activo.'; exit 0 } " +
        "foreach ($x in $a) { Set-DnsClientServerAddress -InterfaceAlias $x.Name -ServerAddresses @('94.140.14.14','94.140.15.15') -ErrorAction Stop }; " +
        "Write-Output 'Aplicado en adaptadores activos.' " +
        "} catch { Write-Error $_.Exception.Message; exit 1 }";

    private const string DnsDhcpResetScript =
        "try { " +
        "Get-NetAdapter | Where-Object { $_.Status -eq 'Up' } | ForEach-Object { " +
        "Set-DnsClientServerAddress -InterfaceIndex $_.InterfaceIndex -ResetServerAddresses -ErrorAction SilentlyContinue }; " +
        "Write-Output 'DNS restaurado a DHCP en interfaces activas.' " +
        "} catch { Write-Error $_.Exception.Message; exit 1 }";
}
