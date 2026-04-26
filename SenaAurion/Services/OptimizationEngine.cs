using SenaAurion.Models;

namespace SenaAurion.Services;

/// <summary>Orquesta registro, servicios y energÃ­a con protecciÃ³n Wiâ€‘Fi y logging.</summary>
public sealed class OptimizationEngine : IOptimizationEngine
{
    private readonly IOptimizationLogger _log;
    private readonly RegistryService _registry;
    private readonly WindowsServiceOptimizer _services;
    private readonly CleanerOptimization _cleaner;
    private readonly NetworkQuickActionService _networkQuick;

    public OptimizationEngine(
        IOptimizationLogger log,
        RegistryService registry,
        WindowsServiceOptimizer services)
    {
        _log = log;
        _registry = registry;
        _services = services;
        _cleaner = new CleanerOptimization(log);
        _networkQuick = new NetworkQuickActionService(log);
    }

    public async Task RunAsync(
        OptimizationDataDocument data,
        OptimizationRunOptions options,
        CancellationToken cancellationToken = default)
    {
        await _log.LogAsync("Motor", "Run", "Inicio", cancellationToken).ConfigureAwait(false);

        var wifiActive = NetworkHardwareDetector.IsWirelessAdapterActive();
        await _log.LogAsync("Motor", "Red",
            wifiActive ? "WiFi activo proteccion estricta" : "Sin WiFi activo politica estandar",
            cancellationToken).ConfigureAwait(false);

        var critical = new HashSet<string>(
            data.NetworkTcp.WifiCriticalServices.Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);

        if (options.ApplyInput)
        {
            foreach (var t in data.InputLatency.Tweaks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _registry.ApplyTweakAsync(t, cancellationToken).ConfigureAwait(false);
            }
        }

        if (options.ApplyNetworkTcp)
        {
            foreach (var t in data.NetworkTcp.Tweaks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _registry.ApplyTweakAsync(t, cancellationToken).ConfigureAwait(false);
            }
        }

        if (options.ApplyRegistryMisc)
        {
            foreach (var t in data.RegistryTweaks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _registry.ApplyTweakAsync(t, cancellationToken).ConfigureAwait(false);
            }
        }

        if (options.ApplyServices)
        {
            foreach (var svc in data.Services)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var userWants = options.ServiceDisableSelections.TryGetValue(svc.Id, out var v) && v;

                var inCriticalList = critical.Contains(svc.ServiceName);
                var blockWifi = wifiActive && (inCriticalList || svc.NeverDisableWhenWifi);

                if (wifiActive && inCriticalList && userWants)
                {
                    await _log.LogAsync("Motor", $"BloqueoWiFi:{svc.ServiceName}",
                        "Lista wifiCriticalServices intento bloqueado aunque el usuario lo marca", cancellationToken)
                        .ConfigureAwait(false);
                }

                await _services.ApplyDisableIfRequestedAsync(svc, userWants, blockWifi, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        SystemStateMonitor.NotifyStateChanged();

        await _log.LogAsync("Motor", "Run", "Finalizado", cancellationToken).ConfigureAwait(false);
    }

    public Task ApplyModuleAsync(string moduleName, OptimizationDataDocument data, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask; // Unused, logic moved to MainViewModel
    }

    public async Task RevertModuleAsync(string moduleName, OptimizationDataDocument data, CancellationToken cancellationToken = default)
    {
        await _log.LogAsync("Motor", $"RevertModule: {moduleName}", "Inicio", cancellationToken).ConfigureAwait(false);
        
        switch (moduleName)
        {
            case "Input":
            case "input":
                await _registry.RevertInputTweaksAsync(data.InputLatency.Tweaks, cancellationToken).ConfigureAwait(false);
                break;
            case "Network":
            case "network":
                await _registry.RevertTcpTweaksAsync(data.NetworkTcp.Tweaks, cancellationToken).ConfigureAwait(false);
                break;
            case "Services":
            case "services":
                await _services.RevertServicesAsync(data.Services, cancellationToken).ConfigureAwait(false);
                break;
        }

        SystemStateMonitor.NotifyStateChanged();
        await _log.LogAsync("Motor", $"RevertModule: {moduleName}", "Finalizado", cancellationToken).ConfigureAwait(false);
    }

    public async Task ApplyTweaksAsync(IEnumerable<RegistryTweakDefinition> tweaks, CancellationToken token = default)
    {
        foreach (var t in tweaks) await _registry.ApplyTweakAsync(t, token).ConfigureAwait(false);
        SystemStateMonitor.NotifyStateChanged();
    }

    public async Task RevertInputTweaksAsync(IEnumerable<RegistryTweakDefinition> tweaks, CancellationToken token = default)
    {
        if (tweaks.Any()) await _registry.RevertInputTweaksAsync(tweaks, token).ConfigureAwait(false);
        SystemStateMonitor.NotifyStateChanged();
    }

    public async Task RevertNetworkTweaksAsync(IEnumerable<RegistryTweakDefinition> tweaks, CancellationToken token = default)
    {
        if (tweaks.Any()) await _registry.RevertTcpTweaksAsync(tweaks, token).ConfigureAwait(false);
        SystemStateMonitor.NotifyStateChanged();
    }

    public async Task ApplyServicesAsync(IEnumerable<ServiceDefinition> services, CancellationToken token = default)
    {
        var wifiActive = NetworkHardwareDetector.IsWirelessAdapterActive();
        foreach (var svc in services)
        {
            await _services.ApplyDisableIfRequestedAsync(svc, true, wifiActive && svc.NeverDisableWhenWifi, token).ConfigureAwait(false);
        }
        SystemStateMonitor.NotifyStateChanged();
    }

    public async Task RevertServicesAsync(IEnumerable<ServiceDefinition> services, CancellationToken token = default)
    {
        await _services.RevertServicesAsync(services, token).ConfigureAwait(false);
        SystemStateMonitor.NotifyStateChanged();
    }

    public async Task ApplyCleanerAsync(IEnumerable<CleanerTaskDefinition> tasks, CancellationToken token = default)
    {
        foreach (var t in tasks)
        {
            await _cleaner.ApplyCleanTaskAsync(t, token).ConfigureAwait(false);
        }
        SystemStateMonitor.NotifyStateChanged();
    }

    public async Task RevertCleanerAsync(IEnumerable<CleanerTaskDefinition> tasks, CancellationToken token = default)
    {
        // Los elementos limpiados no se pueden recuperar sin shadow copies o software forense.
        // Hacemos log simulado de imposibilidad de reversiÃ³n para mantener el estÃ¡ndar.
        await _log.LogAsync("Limpieza", "Reversion", "Archivos eliminados no pueden revertirse mediante esta utilidad. Omitiendo...", token).ConfigureAwait(false);
        SystemStateMonitor.NotifyStateChanged();
    }

    public async Task ApplyNetworkQuickActionsAsync(IEnumerable<NetworkQuickActionDefinition> actions, CancellationToken token = default)
    {
        foreach (var a in actions)
        {
            token.ThrowIfCancellationRequested();
            await _networkQuick.ApplyAsync(a, token).ConfigureAwait(false);
        }
    }

    public async Task ApplyNetworkQuickActionAsync(NetworkQuickActionDefinition action, CancellationToken token = default)
    {
        await _networkQuick.ApplyAsync(action, token).ConfigureAwait(false);
    }
}

