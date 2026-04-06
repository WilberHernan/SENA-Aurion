using VisionaryOptimizer.Models;

namespace VisionaryOptimizer.Services;

public sealed class OptimizationRunOptions
{
    public bool ApplyInput { get; init; } = true;

    public bool ApplyNetworkTcp { get; init; } = true;

    public bool ApplyRegistryMisc { get; init; } = true;

    public bool ApplyServices { get; init; } = true;

    /// <summary>ServicioId → usuario quiere deshabilitar.</summary>
    public IReadOnlyDictionary<string, bool> ServiceDisableSelections { get; init; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
}

public interface IOptimizationEngine
{
    Task RunAsync(OptimizationDataDocument data, OptimizationRunOptions options, CancellationToken cancellationToken = default);

    Task ApplyModuleAsync(string moduleName, OptimizationDataDocument data, CancellationToken cancellationToken = default);
    Task RevertModuleAsync(string moduleName, OptimizationDataDocument data, CancellationToken cancellationToken = default);
    Task ApplyTweaksAsync(IEnumerable<RegistryTweakDefinition> tweaks, CancellationToken token = default);
    Task RevertInputTweaksAsync(IEnumerable<RegistryTweakDefinition> tweaks, CancellationToken token = default);
    Task RevertNetworkTweaksAsync(IEnumerable<RegistryTweakDefinition> tweaks, CancellationToken token = default);
    Task ApplyServicesAsync(IEnumerable<ServiceDefinition> services, CancellationToken token = default);
    Task RevertServicesAsync(IEnumerable<ServiceDefinition> services, CancellationToken token = default);
    
    Task ApplyCleanerAsync(IEnumerable<CleanerTaskDefinition> tasks, CancellationToken token = default);
    Task RevertCleanerAsync(IEnumerable<CleanerTaskDefinition> tasks, CancellationToken token = default);
}
