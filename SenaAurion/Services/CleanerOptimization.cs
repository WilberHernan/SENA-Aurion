using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;
using SenaAurion.Models;

namespace SenaAurion.Services;

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
                "recycle-bin" => string.Empty,
                "event-logs" => GetEventLogsState(),
                "user-folders" => GetUserFoldersState(),
                "thumbcache" => GetThumbCacheState(),
                "delivery-opt" => GetDeliveryOptState(),
                "icon-cache" => GetIconCacheState(),
                "d3d-shader-cache" => GetD3dShaderCacheState(),
                "wer-reports" => GetWerReportsState(),
                "inet-cache" => GetInetCacheState(),
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
        // Asume un threshold donde la mayorÃ­a de los logs mÃ­nimos miden ~20-30MB que Windows retiene permanentemente
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
        if (total == 0) return "0 GB total (Docs: 0 MB, Descargas: 0 MB, Musica: 0 MB, Videos: 0 MB)";
        return $"{FormatBytes(total)} total (Docs: {FormatBytes(doc)}, Descargas: {FormatBytes(dl)}, Musica: {FormatBytes(mus)}, Videos: {FormatBytes(vid)})";
    }

    private static string GetThumbCacheState()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer");
        if (!Directory.Exists(dir)) return "Sin carpeta Explorer local";
        long size = 0;
        int n = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "thumbcache_*.db", SearchOption.TopDirectoryOnly))
            {
                try { size += new FileInfo(f).Length; n++; } catch { }
            }
        }
        catch { }
        return n == 0 ? "Sin miniaturas en caché" : $"{n} archivos · {FormatBytes(size)}";
    }

    private static string GetDeliveryOptState()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache");
        if (!Directory.Exists(path)) return "Caché Delivery Optimization no encontrada";
        return FormatBytes(GetDirectorySize(path));
    }

    private static string GetIconCacheState()
    {
        long size = 0;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var db = Path.Combine(local, "IconCache.db");
        if (File.Exists(db))
        {
            try { size += new FileInfo(db).Length; } catch { }
        }
        var exp = Path.Combine(local, @"Microsoft\Windows\Explorer");
        if (Directory.Exists(exp))
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(exp, "iconcache*.db", SearchOption.TopDirectoryOnly))
                {
                    try { size += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
        }
        return size == 0 ? "Iconos: sin datos accesibles" : $"Iconos en caché · {FormatBytes(size)}";
    }

    private static string GetD3dShaderCacheState()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D3DSCache");
        if (!Directory.Exists(path)) return "D3DSCache no encontrado";
        return FormatBytes(GetDirectorySize(path));
    }

    private static string GetWerReportsState()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\WER");
        if (!Directory.Exists(path)) return "WER no encontrado";
        return FormatBytes(GetDirectorySize(path));
    }

    private static string GetInetCacheState()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\INetCache");
        if (!Directory.Exists(path)) return "INetCache no encontrado";
        return FormatBytes(GetDirectorySize(path));
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
                "thumbcache" => CleanThumbCache(),
                "delivery-opt" => CleanDeliveryOptimizationCache(),
                "icon-cache" => CleanIconCache(),
                "d3d-shader-cache" => CleanD3dShaderCache(),
                "wer-reports" => CleanWerReports(),
                "inet-cache" => CleanInetCache(),
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

            return $"CachÃ© de WU vaciada ({count} elementos)";
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
        
        // Retorna 0 si es exitoso o 0x80004005 si la papelera ya estaba vacÃ­a
        if (result == 0 || result == unchecked((int)0x80004005))
            return "Papelera vaciada";

        return $"Error vaciando papelera (CÃ³digo HRESULT: {result})";
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
                return $"Registros de eventos purgados ({logs.Length} categorÃ­as revisadas)";
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

        return $"ATENCIÃ“N: Carpetas de usuario vaciadas. {count} elementos eliminados de forma permanente.";
    }

    private static string CleanThumbCache()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer");
        if (!Directory.Exists(dir)) return "Carpeta Explorer no encontrada";
        int n = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "thumbcache_*.db", SearchOption.TopDirectoryOnly))
            {
                try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); n++; }
                catch { }
            }
        }
        catch (Exception ex) { return $"Miniaturas: {ex.Message}"; }
        return $"Miniaturas: eliminados {n} archivos (se regeneran al navegar carpetas)";
    }

    private static string CleanDeliveryOptimizationCache()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache");
        if (!Directory.Exists(path)) return "Caché Delivery Optimization no encontrada";
        try
        {
            int n = DeleteDirectoryContents(path);
            return $"Delivery Optimization: {n} elementos eliminados";
        }
        catch (Exception ex) { return $"Delivery Optimization: {ex.Message}"; }
    }

    private static string CleanIconCache()
    {
        int n = 0;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var db = Path.Combine(local, "IconCache.db");
        try
        {
            if (File.Exists(db)) { File.SetAttributes(db, FileAttributes.Normal); File.Delete(db); n++; }
        }
        catch { }
        var exp = Path.Combine(local, @"Microsoft\Windows\Explorer");
        if (Directory.Exists(exp))
        {
            foreach (var f in Directory.EnumerateFiles(exp, "iconcache*.db", SearchOption.TopDirectoryOnly))
            {
                try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); n++; }
                catch { }
            }
        }
        return n == 0
            ? "Iconos: ningún archivo eliminado (puede estar en uso; reinicia y reintenta)"
            : $"Iconos: {n} cachés eliminados (reinicio recomendado para refrescar)";
    }

    private static string CleanD3dShaderCache()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "D3DSCache");
        if (!Directory.Exists(path)) return "D3DSCache no encontrado";
        try
        {
            int n = DeleteDirectoryContents(path);
            return $"Sombreadores DirectX: {n} elementos · se recompilarán al ejecutar juegos";
        }
        catch (Exception ex) { return $"D3DSCache: {ex.Message}"; }
    }

    private static string CleanWerReports()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Windows\WER");
        if (!Directory.Exists(root)) return "WER no encontrado";
        try
        {
            int n = DeleteDirectoryContents(root);
            return $"Informes WER: {n} elementos eliminados";
        }
        catch (Exception ex) { return $"WER: {ex.Message}"; }
    }

    private static string CleanInetCache()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\INetCache");
        if (!Directory.Exists(path)) return "INetCache no encontrado";
        try
        {
            int n = DeleteDirectoryContents(path);
            return $"Caché Internet (INetCache): {n} elementos";
        }
        catch (Exception ex) { return $"INetCache: {ex.Message}"; }
    }

    private static int DeleteDirectoryContents(string targetDir)
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
