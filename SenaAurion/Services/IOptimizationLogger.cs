namespace SenaAurion.Services;

/// <summary>Registro estructurado: [HORA] [MÃ“DULO] [ACCIÃ“N] [RESULTADO].</summary>
public interface IOptimizationLogger
{
    Task LogAsync(string module, string action, string result, CancellationToken cancellationToken = default);

    string LogFilePath { get; }
}

