using System.Globalization;

namespace SenaAurion.Services;

/// <summary>Escribe un .log en la carpeta de la aplicaciÃ³n (offline).</summary>
public sealed class FileOptimizationLogger : IOptimizationLogger, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly StreamWriter _writer;
    private bool _disposed;

    public FileOptimizationLogger()
    {
        var dir = AppContext.BaseDirectory;
        var path = Path.Combine(dir, "SenaAurion.log");
        LogFilePath = path;
        _writer = new StreamWriter(
            new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read),
            System.Text.Encoding.UTF8)
        {
            AutoFlush = true,
        };
        WriteHeaderIfNew();
    }

    public string LogFilePath { get; }

    private void WriteHeaderIfNew()
    {
        try
        {
            var fi = new FileInfo(LogFilePath);
            if (fi.Length == 0)
            {
                _writer.WriteLine($"# Visionary Optimizer Â· inicio {DateTime.Now.ToString("u", CultureInfo.InvariantCulture)}");
                _writer.WriteLine("# Formato: [HORA] [MÃ“DULO] [ACCIÃ“N] [RESULTADO]");
            }
        }
        catch
        {
            /* no bloquear arranque */
        }
    }

    public async Task LogAsync(string module, string action, string result, CancellationToken cancellationToken = default)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
        var line = $"[{ts}] [{module}] [{action}] [{result}]";
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(line).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _writer.Dispose();
        _gate.Dispose();
    }
}

