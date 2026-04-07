using System.ServiceProcess;
using Microsoft.Win32;
using SenaAurion.Models;

namespace SenaAurion.Services;

/// <summary>GestiÃ³n de servicios con ServiceController + registro Start; sin depender solo de consola.</summary>
public sealed class WindowsServiceOptimizer
{
    private const int StartDisabled = 4;

    private readonly IOptimizationLogger _log;

    public WindowsServiceOptimizer(IOptimizationLogger log) => _log = log;

    public async Task ApplyDisableIfRequestedAsync(
        ServiceDefinition definition,
        bool userWantsDisable,
        bool blockByWifi,
        CancellationToken cancellationToken = default)
    {
        var name = definition.ServiceName;
        await _log.LogAsync("Servicios", $"Evaluar:{name}", "Inicio", cancellationToken).ConfigureAwait(false);

        if (!userWantsDisable)
        {
            await _log.LogAsync("Servicios", $"Evaluar:{name}", "Usuario no solicitÃ³ deshabilitar Â· omitido", cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        if (blockByWifi)
        {
            await _log.LogAsync("Servicios", $"BloqueoWiFi:{name}",
                "Adaptador Wiâ€‘Fi activo Â· no se modifica servicio crÃ­tico", cancellationToken).ConfigureAwait(false);
            return;
        }

        var outcome = await Task.Run(() => TryDisableService(name), cancellationToken).ConfigureAwait(false);
        await _log.LogAsync("Servicios", $"Deshabilitar:{name}", outcome, cancellationToken).ConfigureAwait(false);
    }

    public async Task RevertServicesAsync(IEnumerable<ServiceDefinition> services, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => {
            // Valores por defecto de Windows para servicios conocidos (2=Auto, 3=Manual).
            // Si el servicio no estÃ¡ acÃ¡, lo regresamos al de fÃ¡brica por defecto Manual (3).
            var defaults = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "SysMain", 2 },
                { "DiagTrack", 2 },
                { "WSearch", 2 },
                { "WaaSMedicSvc", 3 },
                { "DiagTrackSvc", 2 },
                { "Spooler", 2 },
                { "wuauserv", 3 },
                { "Bits", 3 },
                { "XboxGipSvc", 3 },
                { "XblGameSave", 3 },
                { "XblAuthManager", 3 },
                { "XboxNetApiSvc", 3 }
            };

            foreach (var svc in services)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(
                        $@"SYSTEM\CurrentControlSet\Services\{svc.ServiceName}", writable: true);
                    if (key != null)
                    {
                        var targetStartMethod = defaults.TryGetValue(svc.ServiceName, out var customVal) ? customVal : 3;

                        // Solo cambiamos al valor original si estÃ¡ totalmente deshabilitado.
                        var currentStart = key.GetValue("Start") as int? ?? -1;
                        if (currentStart == StartDisabled)
                        {
                            key.SetValue("Start", targetStartMethod, RegistryValueKind.DWord);
                        }

                        // Try to start the service back if it was vital and we restored it to Auto (2)
                        if (targetStartMethod == 2)
                        {
                            try
                            {
                                using var sc = new ServiceController(svc.ServiceName);
                                if (sc.Status == ServiceControllerStatus.Stopped)
                                {
                                    sc.Start();
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
        }, cancellationToken);
    }

    private string TryDisableService(string serviceName)
    {
        try
        {
            using var sc = new ServiceController(serviceName);
            _ = sc.Status;
        }
        catch (InvalidOperationException)
        {
            return "Servicio no encontrado Â· omitido";
        }

        var start = GetServiceStartType(serviceName);
        
        try
        {
            using var sc = new ServiceController(serviceName);
            bool isRunning = sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending;
            if (isRunning)
            {
                // Un servicio Running, especialmente Spooler o Windows Update no deja modificarse facilmente si estÃ¡ en uso. Matar dependencias es un poco bruto pero Stop() con Timeout es estÃ¡ndar
                if (sc.CanStop)
                {
                    sc.Stop();
                    // Don't fully block thread forever, timeout 10 seconds is acceptable for services
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                }
            }

            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: true);
            if (key is null)
                return "Clave de servicio no encontrada - Saltando";

            if (start != StartDisabled)
            {
                key.SetValue("Start", StartDisabled, RegistryValueKind.DWord);
            }

            return "Deshabilitado";
        }
        catch (Exception ex)
        {
            return $"Error:{ex.Message}";
        }
    }

    private static int? GetServiceStartType(string serviceName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
            var o = key?.GetValue("Start");
            return o as int? ?? (o is long l ? (int)l : null);
        }
        catch
        {
            return null;
        }
    }
}

