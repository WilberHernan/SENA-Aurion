using SenaAurion.Models;

namespace SenaAurion.Services;

public interface IOptimizationDataProvider
{
    Task<OptimizationDataDocument?> LoadAsync(CancellationToken cancellationToken = default);
}

