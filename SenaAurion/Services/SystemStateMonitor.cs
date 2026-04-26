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
            string kbdSpeed = GetRegistryString(Registry.CurrentUser, @"Control Panel\Keyboard", "KeyboardSpeed") ?? "31";
            string kbdDelay = GetRegistryString(Registry.CurrentUser, @"Control Panel\Keyboard", "KeyboardDelay") ?? "1";
            string mouseSpeed = GetRegistryString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseSpeed") ?? "10";
            string wheelScroll = GetRegistryString(Registry.CurrentUser, @"Control Panel\Desktop", "WheelScrollLines") ?? "3";
            string menuDelay = GetRegistryString(Registry.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay") ?? "400";
            string mouHover = GetRegistryString(Registry.CurrentUser, @"Control Panel\Mouse", "MouseHoverTime") ?? "400";

            // Valores objetivo del perfil institucional SENA
            bool kbdSpeedOk = kbdSpeed == "31";
            bool kbdDelayOk = kbdDelay == "0";
            bool mouseSpeedOk = mouseSpeed == "12";
            bool wheelScrollOk = wheelScroll == "6";
            bool menuDelayOk = menuDelay == "50";
            bool mouHoverOk = mouHover == "100";

            int score = 0;
            if (kbdSpeedOk) score++;
            if (kbdDelayOk) score++;
            if (mouseSpeedOk) score++;
            if (wheelScrollOk) score++;
            if (menuDelayOk) score++;
            if (mouHoverOk) score++;

            string statusEmoji = score switch
            {
                6 => "ðŸŸ¢",
                >= 3 => "ðŸŸ¡",
                _ => "ðŸŸ "
            };

            string statusText = score switch
            {
                6 => "Entrada optimizada",
                >= 3 => "Entrada parcial",
                _ => "Ajustes estÃ¡ndar"
            };

            return $"{statusEmoji} {statusText} | Teclado: {kbdSpeed}/{kbdDelay} | RatÃ³n: {mouseSpeed} | Scroll: {wheelScroll} | MenÃºs: {menuDelay}ms | Hover: {mouHover}ms";
        }
    }

    public static string GetNetworkState()
    {
        lock (_lock)
        {
            bool isWifi = NetworkHardwareDetector.IsWirelessAdapterActive();
            int? tcpAck = GetFirstInterfaceRegistryDWord(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", "TcpAckFrequency");
            int? tcpNoDelay = GetFirstInterfaceRegistryDWord(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces", "TCPNoDelay");
            int? tcp1323 = GetRegistryDWord(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", "Tcp1323Opts");
            int? throttling = GetRegistryDWord(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex");
            int? lmhosts = GetRegistryDWord(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\NetBT\Parameters", "EnableLMHosts");

            bool ackOk = tcpAck == 1;
            bool noDelayOk = tcpNoDelay == 1;
            bool tcp1323Ok = tcp1323 == 1;
            bool throttlingOk = throttling == -1 || throttling == 0xFFFFFFFF;
            bool lmhostsOk = lmhosts == 0;

            int score = 0;
            if (ackOk) score++;
            if (noDelayOk) score++;
            if (tcp1323Ok) score++;
            if (throttlingOk) score++;
            if (lmhostsOk) score++;

            string statusEmoji = score switch
            {
                >= 4 => "ðŸŸ¢",
                >= 2 => "ðŸŸ¡",
                _ => "ðŸŸ "
            };

            string statusText = score switch
            {
                >= 4 => "Red optimizada",
                >= 2 => "Red parcial",
                _ => "Ajustes originales"
            };

            return $"{statusEmoji} {statusText} | Enlace: {(isWifi ? "Wi-Fi" : "Ethernet")} | ACK: {(ackOk ? "1" : "-")} / NoDelay: {(noDelayOk ? "1" : "-")} / RFC1323: {(tcp1323Ok ? "1" : "-")} | Throttling: {(throttlingOk ? "OFF" : "ON")} | LMHOSTS: {(lmhostsOk ? "OFF" : "ON")}";
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

    public static ServiceStatusInfo GetServiceStatusInfo(string serviceName)
    {
        lock (_lock)
        {
            try
            {
                using var sc = new ServiceController(serviceName);
                _ = sc.Status;

                using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
                var start = key?.GetValue("Start");
                int startType = start as int? ?? (start is long l ? (int)l : -1);
                var startText = startType switch { 2 => "Auto", 3 => "Manual", 4 => "Deshabilitado", _ => "Desconocido" };

                var isRunning = sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending;
                var isStopped = sc.Status == ServiceControllerStatus.Stopped || sc.Status == ServiceControllerStatus.StopPending;
                var isDisabled = startType == 4;

                return new ServiceStatusInfo(
                    Exists: true,
                    IsRunning: isRunning,
                    IsStopped: isStopped,
                    IsDisabled: isDisabled,
                    StartTypeText: startText);
            }
            catch
            {
                return new ServiceStatusInfo(
                    Exists: false,
                    IsRunning: false,
                    IsStopped: false,
                    IsDisabled: false,
                    StartTypeText: "No encontrado");
            }
        }
    }

    public readonly record struct ServiceStatusInfo(
        bool Exists,
        bool IsRunning,
        bool IsStopped,
        bool IsDisabled,
        string StartTypeText);

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

    private static int? GetRegistryDWord(RegistryKey root, string subKey, string valueName)
    {
        try
        {
            using var key = root.OpenSubKey(subKey, false);
            var val = key?.GetValue(valueName);
            if (val == null) return null;
            return val as int? ?? (val is long l ? (int)l : null);
        }
        catch { return null; }
    }

    public static string GetServicesState(IEnumerable<string> serviceNames)
    {
        lock (_lock)
        {
            int total = 0;
            int disabled = 0;
            int active = 0;
            int notFound = 0;

            foreach (var name in serviceNames)
            {
                try
                {
                    using var sc = new ServiceController(name);
                    _ = sc.Status;
                    using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
                    var start = key?.GetValue("Start");
                    int startType = start as int? ?? (start is long l ? (int)l : -1);

                    total++;
                    if (startType == 4)
                        disabled++;
                    else
                        active++;
                }
                catch
                {
                    total++;
                    notFound++;
                }
            }

            string statusEmoji = disabled > active
                ? "ðŸŸ¢"
                : disabled > 0 ? "ðŸŸ¡" : "ðŸŸ ";

            string statusText = disabled > active
                ? "Servicios optimizados"
                : disabled > 0 ? "Servicios parcialmente optimizados" : "Servicios activos";

            return $"{statusEmoji} {statusText} | Total: {total} | Deshabilitados: {disabled} | Activos: {active} | No encontrados: {notFound}";
        }
    }
}
