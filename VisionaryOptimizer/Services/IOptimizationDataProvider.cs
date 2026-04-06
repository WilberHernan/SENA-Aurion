using VisionaryOptimizer.Models;

namespace VisionaryOptimizer.Services;

public interface IOptimizationDataProvider
{
    Task<OptimizationDataDocument?> LoadAsync(CancellationToken cancellationToken = default);
}
