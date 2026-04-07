using System.Text.Json;
using Microsoft.Win32;
using SenaAurion.Models;

namespace SenaAurion.Services;

/// <summary>Acceso al registro con comprobaciÃ³n previa y registro de incidencias.</summary>
public sealed class RegistryService
{
    private readonly IOptimizationLogger _log;

    public RegistryService(IOptimizationLogger log) => _log = log;

    public async Task ApplyTweakAsync(RegistryTweakDefinition tweak, CancellationToken cancellationToken = default)
    {
        await _log.LogAsync("Registry", $"Aplicar:{tweak.Id}", "Inicio", cancellationToken).ConfigureAwait(false);

        var outcome = await Task.Run(() => ApplyTweakCore(tweak), cancellationToken).ConfigureAwait(false);

        await _log.LogAsync("Registry", $"Aplicar:{tweak.Id}", outcome, cancellationToken).ConfigureAwait(false);
    }

    public async Task RevertInputTweaksAsync(IEnumerable<RegistryTweakDefinition> tweaks, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => {
            // Valores por defecto estructurales de Windows. Si no estÃ¡ aquÃ­, se elimina para que el SO use NDIS u o default en memoria.
            var defaults = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "MenuShowDelay", "400" },
                { "KeyboardDelay", "1" },
                { "KeyboardSpeed", "31" },
                { "MouseSpeed", "10" },
                { "MouseHoverTime", "400" },
                { "MouseDataQueueSize", 100 },
                { "KeyboardDataQueueSize", 100 }
            };

            foreach (var tweak in tweaks)
            {
                try
                {
                    using var root = GetRootKey(tweak.Hive);
                    using var writable = root?.OpenSubKey(tweak.SubKey, writable: true);
                    if (writable != null)
                    {
                        if (defaults.TryGetValue(tweak.ValueName, out var defVal))
                        {
                            var kind = ParseValueKind(tweak.ValueKind);
                            writable.SetValue(tweak.ValueName, defVal, kind);
                        }
                        else
                        {
                            // Si no es un valor crÃ­tico conocido, eliminar la clave forzando el comportamiento nativo
                            writable.DeleteValue(tweak.ValueName, throwOnMissingValue: false);
                        }
                    }
                }
                catch { }
            }
        }, cancellationToken);
    }

    public async Task RevertTcpTweaksAsync(IEnumerable<RegistryTweakDefinition> tweaks, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => {
            // Valores por defecto de Windows para Network/Multimedia (Si existen y no son de red puros)
            var defaults = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                { "NetworkThrottlingIndex", 10 },
                { "SystemResponsiveness", 20 },
                { "MaxUserPort", 65534 },
                { "TcpTimedWaitDelay", 30 } // default varies 30-120
            };

            foreach (var tweak in tweaks)
            {
                try
                {
                    using var root = GetRootKey(tweak.Hive);
                    if (root == null) continue;

                    // Si es interface global, borrar la clave para que use la por defecto de NDIS
                    if (tweak.SubKey.EndsWith(@"Tcpip\Parameters\Interfaces", StringComparison.OrdinalIgnoreCase))
                    {
                        using var interfaceKey = root.OpenSubKey(tweak.SubKey, writable: true);
                        if (interfaceKey != null)
                        {
                            foreach (var ifaceName in interfaceKey.GetSubKeyNames())
                            {
                                using var ifaceNode = interfaceKey.OpenSubKey(ifaceName, writable: true);
                                ifaceNode?.DeleteValue(tweak.ValueName, false);
                            }
                        }
                    }
                    else
                    {
                        using var writable = root.OpenSubKey(tweak.SubKey, writable: true);
                        if (writable != null)
                        {
                            if (defaults.TryGetValue(tweak.ValueName, out var defVal))
                            {
                                var kind = ParseValueKind(tweak.ValueKind);
                                writable.SetValue(tweak.ValueName, defVal, kind);
                            }
                            else
                            {
                                writable.DeleteValue(tweak.ValueName, false);
                            }
                        }
                    }
                }
                catch { }
            }
        }, cancellationToken);
    }

    /// <summary>Regla de oro: si la clave no existe, no se crea; se devuelve mensaje para el log.</summary>
    private static string ApplyTweakCore(RegistryTweakDefinition tweak)
    {
        try
        {
            using var root = GetRootKey(tweak.Hive);
            if (root is null)
                return "Colmena no soportada Â· omitido";

            // Interceptar configuraciones per-interface para protocolos de Red (como las guÃ­as estÃ¡ndar de Leatrix/Fr33thy)
            if (tweak.SubKey.EndsWith(@"Tcpip\Parameters\Interfaces", StringComparison.OrdinalIgnoreCase))
            {
                using var interfaceKey = root.OpenSubKey(tweak.SubKey, writable: true);
                if (interfaceKey is null) return "Sin acceso de escritura Â· interfaces";

                int appliedCount = 0;
                var kind = ParseValueKind(tweak.ValueKind);
                var value = ConvertValue(tweak.ValueData, kind);

                foreach (var ifaceName in interfaceKey.GetSubKeyNames())
                {
                    using var ifaceNode = interfaceKey.OpenSubKey(ifaceName, writable: true);
                    if (ifaceNode != null)
                    {
                        ifaceNode.SetValue(tweak.ValueName, value, kind);
                        appliedCount++;
                    }
                }
                return appliedCount > 0 ? $"OK en {appliedCount} interfaces" : "No hay interfaces";
            }

            // Flujo estÃ¡ndar Check-Before-Action
            using var probe = root.OpenSubKey(tweak.SubKey, writable: false);
            if (probe is null)
                return "Clave no encontrada - Saltando";

            using var writable = root.OpenSubKey(tweak.SubKey, writable: true);
            if (writable is null)
                return "Sin acceso de escritura Â· omitido";

            var defaultKind = ParseValueKind(tweak.ValueKind);
            var defaultValue = ConvertValue(tweak.ValueData, defaultKind);
            writable.SetValue(tweak.ValueName, defaultValue, defaultKind);
            return "OK";
        }
        catch (Exception ex)
        {
            return $"Error:{ex.Message}";
        }
    }

    public static RegistryKey? GetRootKey(string hive) =>
        hive.Trim() switch
        {
            "CurrentUser" => Registry.CurrentUser,
            "LocalMachine" => Registry.LocalMachine,
            "Users" => Registry.Users,
            "ClassesRoot" => Registry.ClassesRoot,
            _ => null,
        };

    public static RegistryValueKind ParseValueKind(string kind) =>
        kind.Trim() switch
        {
            "String" or "REG_SZ" => RegistryValueKind.String,
            "DWord" or "REG_DWORD" => RegistryValueKind.DWord,
            "QWord" or "REG_QWORD" => RegistryValueKind.QWord,
            "ExpandString" or "REG_EXPAND_SZ" => RegistryValueKind.ExpandString,
            "MultiString" or "REG_MULTI_SZ" => RegistryValueKind.MultiString,
            "Binary" or "REG_BINARY" => RegistryValueKind.Binary,
            _ => RegistryValueKind.String,
        };

    public static object ConvertValue(JsonElement? data, RegistryValueKind kind)
    {
        if (data is null || !data.HasValue)
            return string.Empty;

        var el = data.Value;
        if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return string.Empty;
        return kind switch
        {
            RegistryValueKind.DWord => el.ValueKind == JsonValueKind.Number
                ? el.GetInt32()
                : int.TryParse(el.GetString(), out var d) ? d : 0,
            RegistryValueKind.QWord => el.ValueKind == JsonValueKind.Number
                ? el.GetInt64()
                : long.TryParse(el.GetString(), out var q) ? q : 0L,
            RegistryValueKind.MultiString => el.ValueKind == JsonValueKind.Array
                ? el.EnumerateArray().Select(e => e.GetString() ?? "").ToArray()
                : new[] { el.GetString() ?? "" },
            _ => el.ValueKind == JsonValueKind.Number
                ? el.GetRawText()
                : el.GetString() ?? "",
        };
    }
}

