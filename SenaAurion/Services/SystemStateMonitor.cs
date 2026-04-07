using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SenaAurion.Services;

public static class SystemStateMonitor
{
    private static readonly object _lock = new();

    public static event EventHandler? StateChanged;

    public static void NotifyStateChanged() => StateChanged?.Invoke(null, EventArgs.Empty);

    public static string GetInputState()
    {
        lock (_lock)
        {
            string menuDelay = GetRegistryString(Registry.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay") ?? "400";
            string kbdSpeed = GetRegistryString(Registry.CurrentUser, @"Control Panel\Keyboard", "KeyboardSpeed") ?? "31";
            string mouDelay = GetRegistryString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseHoverTime") ?? "400";

            bool isOptimized = menuDelay == "0" && mouDelay == "10";
            return $"{(isOptimized ? "ðŸŸ¢ Modo Latencia Baja Activa" : "ðŸŸ  Ajustes EstÃ¡ndar")} | MenÃºs: {menuDelay}ms | MouseHover: {mouDelay}ms";
        }
    }

    public static string GetNetworkState()
    {
        lock (_lock)
        {
            bool isWifi = NetworkHardwareDetector.IsWirelessAdapterActive();
            int? tcpAck = GetFirstInterfaceRegistryDWord(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", "TcpAckFrequency");
            int? tcpNoDelay = GetFirstInterfaceRegistryDWord(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", "TCPNoDelay");

            bool isOptimized = tcpAck == 1 && tcpNoDelay == 1;
            return $"{(isOptimized ? "ðŸŸ¢ Modo Baja Latencia TCP" : "ðŸŸ  Ajustes NDIS Originales")} | Enlace: {(isWifi ? "Wi-Fi" : "Ethernet")} | ACK/NoDelay: {(isOptimized ? "1 (Activo)" : "0/Original")}";
        }
    }

    public static string GetServiceStatus(string serviceName)
    {
        lock (_lock)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                string status = sc.Status == ServiceControllerStatus.Running ? "ðŸŸ¢ EjecutÃ¡ndose" : "ðŸ”´ Detenido";
                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
                var start = key?.GetValue("Start");
                int startType = start as int? ?? (start is long l ? (int)l : -1);
                string startText = startType switch { 2 => "Auto", 3 => "Manual", 4 => "Deshabilitado", _ => "Desconocido" };
                
                // Si el servicio estÃ¡ en manual pero detenido, no es Ã³ptimo ni malo. Pero si estÃ¡ Deshabilitado, claramente es nuestra optimizaciÃ³n.
                return $"{status} | {startText}";
            }
            catch
            {
                return "âšª No encontrado";
            }
        }
    }

    private static string? GetRegistryString(RegistryKey root, string subKey, string valueName)
    {
        try
        {
            using var key = root.OpenSubKey(subKey, false);
            return key?.GetValue(valueName)?.ToString();
        }
        catch { return null; }
    }

    private static int? GetFirstInterfaceRegistryDWord(string interfacesPath, string valueName)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(interfacesPath, false);
            if (key == null) return null;
            foreach (var name in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(name, false);
                var val = sub?.GetValue(valueName);
                if (val != null)
                {
                    return val as int? ?? (val is long l ? (int)l : null);
                }
            }
            return null;
        }
        catch { return null; }
    }
}
