using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using VisionaryOptimizer.Models;

namespace VisionaryOptimizer.Services;

public sealed class CleanerOptimization
{
    private readonly IOptimizationLogger _log;

    [DllImport("shell32.dll")]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private const uint SHERB_NOCONFIRMATION = 1;
    private const uint SHERB_NOPROGRESSUI = 2;
    private const uint SHERB_NOSOUND = 4;

    public CleanerOptimization(IOptimizationLogger log)
    {
        _log = log;
    }

    public async Task ApplyCleanTaskAsync(CleanerTaskDefinition task, CancellationToken token = default)
    {
        await _log.LogAsync("Limpieza", $"Ejecutar:{task.Id}", "Inicio", token).ConfigureAwait(false);

        var outcome = await Task.Run(() => ExecuteTaskCore(task.Id), token).ConfigureAwait(false);

        await _log.LogAsync("Limpieza", $"Finalizar:{task.Id}", outcome, token).ConfigureAwait(false);
    }

    public static string GetTaskState(string id)
    {
        try
        {
            return id switch
            {
                "temp-files" => GetTempState(),
                "prefetch" => GetPrefetchState(),
                "wu-cache" => GetWuCacheState(),
                "recycle-bin" => string.Empty, // Ya no se evalúa dinámicamente
                "event-logs" => GetEventLogsState(),
                "user-folders" => GetUserFoldersState(),
                _ => "Desconocido"
            };
        }
        catch
        {
            return "Error al calcular estado";
        }
    }

    private static string GetTempState()
    {
        long userSize = GetDirectorySize(Path.GetTempPath());
        long sysSize = GetDirectorySize(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"));
        long total = userSize + sysSize;
        return $"Usuario: {FormatBytes(userSize)} | Sistema: {FormatBytes(sysSize)} | Total: {FormatBytes(total)}";
    }

    private static string GetPrefetchState()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
        int count = GetDirectoryFileCount(path);
        return count == 0 ? "0 archivos .pf" : $"{count} archivos .pf";
    }

    private static string GetWuCacheState()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"SoftwareDistribution\Download");
        long size = GetDirectorySize(path);
        return size == 0 ? "0 MB" : $"{FormatBytes(size)}";
    }

    private static string GetEventLogsState()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"System32\winevt\Logs");
        long size = GetDirectorySize(path);
        // Asume un threshold donde la mayoría de los logs mínimos miden ~20-30MB que Windows retiene permanentemente
        if (size <= 1048576 * 30) return "Logs optimizados";
        return $"{FormatBytes(size)} disponibles";
    }

    private static string GetUserFoldersState()
    {
        long doc = GetDirectorySize(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        long mus = GetDirectorySize(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
        long vid = GetDirectorySize(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        long dl = GetDirectorySize(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));

        long total = doc + mus + vid + dl;
        if (total == 0) return "0 GB total (Docs: 0 MB, Descargas: 0 MB, Música: 0 MB, Videos: 0 MB)";
        return $"{FormatBytes(total)} total (Docs: {FormatBytes(doc)}, Descargas: {FormatBytes(dl)}, Música: {FormatBytes(mus)}, Videos: {FormatBytes(vid)})";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1073741824) return $"{bytes / 1073741824.0:F2} GB";
        if (bytes >= 1048576) return $"{bytes / 1048576.0:F2} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} B";
    }

    private static long GetDirectorySize(string path)
    {
        if (!Directory.Exists(path)) return 0;
        long size = 0;
        try
        {
            var dir = new DirectoryInfo(path);
            foreach (var fi in dir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                size += fi.Length;
            }
        }
        catch { }
        return size;
    }

    private static int GetDirectoryFileCount(string path)
    {
        if (!Directory.Exists(path)) return 0;
        try
        {
            return Directory.GetFiles(path, "*.*", SearchOption.TopDirectoryOnly).Length;
        }
        catch { return 0; }
    }

    private string ExecuteTaskCore(string id)
    {
        try
        {
            return id switch
            {
                "temp-files" => CleanTempFiles(),
                "prefetch" => CleanPrefetch(),
                "wu-cache" => CleanWindowsUpdateCache(),
                "recycle-bin" => CleanRecycleBin(),
                "event-logs" => CleanEventLogs(),
                "user-folders" => CleanUserFolders(),
                _ => "Tarea no reconocida"
            };
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private string CleanTempFiles()
    {
        int count = 0;
        string[] tempPaths = { 
            Path.GetTempPath(), 
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") 
        };

        foreach (var path in tempPaths)
        {
            if (Directory.Exists(path))
            {
                count += DeleteDirectoryContents(path);
            }
        }
        return $"Limpios {count} elementos de Temp";
    }

    private string CleanPrefetch()
    {
        string prefetchPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
        if (Directory.Exists(prefetchPath))
        {
            int count = DeleteDirectoryContents(prefetchPath);
            return $"Limpios {count} elementos de Prefetch";
        }
        return "Directorio Prefetch no encontrado";
    }

    private string CleanWindowsUpdateCache()
    {
        try
        {
            StopService("wuauserv");
            StopService("bits");
            Thread.Sleep(2000); // Allow handles to release

            string wuPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"SoftwareDistribution\Download");
            int count = 0;
            if (Directory.Exists(wuPath))
            {
                count = DeleteDirectoryContents(wuPath);
            }

            StartService("wuauserv");
            StartService("bits");

            return $"Caché de WU vaciada ({count} elementos)";
        }
        catch (Exception e)
        {
            return $"Fallo purga WU: {e.Message}";
        }
    }

    private string CleanRecycleBin()
    {
        int result = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
        Thread.Sleep(500); // Give Windows Shell time to update its registry count before the system refreshes states.
        
        // Retorna 0 si es exitoso o 0x80004005 si la papelera ya estaba vacía
        if (result == 0 || result == unchecked((int)0x80004005))
            return "Papelera vaciada";

        return $"Error vaciando papelera (Código HRESULT: {result})";
    }

    private string CleanEventLogs()
    {
        try
        {
            // Se invoca wevtutil para listar y limpiar en paralelo usando Process
            var listProc = new ProcessStartInfo("wevtutil", "el")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(listProc);
            if (p != null)
            {
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                string[] logs = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string log in logs)
                {
                    try
                    {
                        var clProc = new ProcessStartInfo("wevtutil", $"cl \"{log}\"") { CreateNoWindow = true, UseShellExecute = false };
                        Process.Start(clProc)?.WaitForExit(1000);
                    }
                    catch { /* ignorar individuales */ }
                }
                return $"Registros de eventos purgados ({logs.Length} categorías revisadas)";
            }
            return "No se pudo invocar wevtutil";
        }
        catch (Exception ex)
        {
            return $"Error evento log: {ex.Message}";
        }
    }

    private string CleanUserFolders()
    {
        int count = 0;
        Environment.SpecialFolder[] targetFolders = {
            Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.MyMusic,
            Environment.SpecialFolder.MyVideos,
            Environment.SpecialFolder.UserProfile // Downloads require special mapping, getting through registry
        };

        // Limpiar Documentos, Musica, Videos
        foreach (var folder in targetFolders)
        {
            if (folder == Environment.SpecialFolder.UserProfile) continue;
            string path = Environment.GetFolderPath(folder);
            if (Directory.Exists(path))
            {
                count += DeleteDirectoryContents(path);
            }
        }

        // Descargas
        string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (Directory.Exists(downloads))
        {
            count += DeleteDirectoryContents(downloads);
        }

        return $"ATENCIÓN: Carpetas de usuario vaciadas. {count} elementos eliminados de forma permanente.";
    }

    private int DeleteDirectoryContents(string targetDir)
    {
        int deleted = 0;
        if (!Directory.Exists(targetDir)) return deleted;

        try
        {
            string[] files = Directory.GetFiles(targetDir);
            string[] dirs = Directory.GetDirectories(targetDir);

            foreach (string file in files)
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                    deleted++;
                }
                catch { /* Access denied or in use */ }
            }

            foreach (string dir in dirs)
            {
                try
                {
                    deleted += DeleteDirectoryContents(dir);
                    Directory.Delete(dir, false); // empty directory
                    deleted++;
                }
                catch { /* Access denied or in use */ }
            }
        }
        catch
        {
            // Ignorar permisos denegados al enumerar el root node
        }
        return deleted;
    }

    private void StopService(string name)
    {
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.Paused)
            {
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
            }
        }
        catch { }
    }

    private void StartService(string name)
    {
        try
        {
            using var sc = new ServiceController(name);
            if (sc.Status == ServiceControllerStatus.Stopped)
            {
                sc.Start();
                sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            }
        }
        catch { }
    }
}