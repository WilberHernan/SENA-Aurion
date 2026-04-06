using System.Text.Json;
using VisionaryOptimizer.Models;

namespace VisionaryOptimizer.Services;

/// <summary>Carga OptimizationData.json desde el directorio de salida (sin red).</summary>
public sealed class JsonOptimizationDataProvider : IOptimizationDataProvider
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public async Task<OptimizationDataDocument?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "Data", "OptimizationData.json");
        if (!File.Exists(path))
            return null;

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<OptimizationDataDocument>(stream, Options, cancellationToken)
            .ConfigureAwait(false);
    }
}
