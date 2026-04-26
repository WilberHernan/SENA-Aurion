using Microsoft.Win32;
using SenaAurion.Models;

namespace SenaAurion.Services;

public static class RegistryDefaults
{
    // Defaults represent "normal/stock" Windows behavior for common values.
    // If a value is missing, Windows often behaves as if default applies.
    private static readonly Dictionary<string, object> InputDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        { "MenuShowDelay", "400" },
        { "KeyboardDelay", "1" },
        { "KeyboardSpeed", "31" },
        { "MouseSpeed", "10" },
        { "MouseHoverTime", "400" },
        { "MouseHoverHeight", "4" },
        { "MouseHoverWidth", "4" },
        { "MouseDataQueueSize", 100 },
        { "KeyboardDataQueueSize", 100 },
        { "WheelScrollLines", "3" },
        { "MouseTrails", "0" }
    };

    private static readonly Dictionary<string, object> TcpDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        // Interface-scoped values vary; missing is typical default.
        { "Tcp1323Opts", 3 },             // common default (varies)
        { "NetworkThrottlingIndex", 10 }, // 10 = limitar al ~10% (default Windows)
        { "EnableLMHosts", 1 }            // 1 = habilitado (default Windows)
    };

    public static bool TryGetDefaultDisplayValue(RegistryTweakDefinition def, out string display)
    {
        display = "";
        if (TryGetDefaultValue(def, out var v))
        {
            display = RegistryValueFormatter.ToDisplayString(v);
            return true;
        }
        return false;
    }

    public static bool IsEquivalentToDefault(RegistryTweakDefinition def, object currentVal, RegistryValueKind kind)
    {
        if (!TryGetDefaultValue(def, out var defaultVal))
            return false;

        return RegistryValueComparer.AreEquivalent(currentVal, defaultVal, kind);
    }

    public static bool IsWindowsDefaultOptimized(RegistryTweakDefinition def, object? optimizedVal, RegistryValueKind kind)
    {
        // If Windows default equals our optimized value, we can call it "optimizado por Windows".
        if (optimizedVal is null)
            return false;

        if (!TryGetDefaultValue(def, out var defaultVal))
            return false;

        return RegistryValueComparer.AreEquivalent(defaultVal, optimizedVal, kind);
    }

    private static bool TryGetDefaultValue(RegistryTweakDefinition def, out object value)
    {
        value = "";

        // Input tweaks are CU + common names, also the kbd/mouclass ones.
        if (def.Category.Equals("Input", StringComparison.OrdinalIgnoreCase))
        {
            if (InputDefaults.TryGetValue(def.ValueName, out var v))
            {
                value = v;
                return true;
            }
        }

        if (def.Category.Equals("Network", StringComparison.OrdinalIgnoreCase) ||
            def.SubKey.Contains(@"\Tcpip\", StringComparison.OrdinalIgnoreCase) ||
            def.SubKey.Contains(@"\Multimedia\", StringComparison.OrdinalIgnoreCase))
        {
            if (TcpDefaults.TryGetValue(def.ValueName, out var v))
            {
                value = v;
                return true;
            }
        }

        // No known default (do not guess).
        return false;
    }
}

