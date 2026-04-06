namespace VisionaryOptimizer.Services;

/// <summary>Registro estructurado: [HORA] [MÓDULO] [ACCIÓN] [RESULTADO].</summary>
public interface IOptimizationLogger
{
    Task LogAsync(string module, string action, string result, CancellationToken cancellationToken = default);

    string LogFilePath { get; }
}
