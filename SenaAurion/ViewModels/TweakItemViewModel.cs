using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SenaAurion.ViewModels;

public partial class TweakItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _displayName = string.Empty;

    // Optional: for “blocked” suffix coloring
    [ObservableProperty]
    private string _displayNameMain = string.Empty;

    [ObservableProperty]
    private string _blockedSuffix = string.Empty; // ex: " (bloqueado)"

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _currentStateText = string.Empty;

    [ObservableProperty]
    private ImageSource? _iconSource;

    partial void OnDisplayNameChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(DisplayNameMain))
            DisplayNameMain = value;
        if (BlockedSuffix is null)
            BlockedSuffix = string.Empty;
    }

    [ObservableProperty]
    private bool _isPresent = true;

    [ObservableProperty]
    private bool _isSelectable = true;

    [ObservableProperty]
    private string _lastChangeText = string.Empty;

    public Microsoft.UI.Xaml.Visibility LastChangeVisibility =>
        string.IsNullOrWhiteSpace(LastChangeText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

    [ObservableProperty]
    private bool _isDanger;

    /// <summary>Texto para tooltip del icono de advertencia (impacto / riesgo).</summary>
    [ObservableProperty]
    private string _dangerTooltip = "";

    public Microsoft.UI.Xaml.Visibility DangerVisibility =>
        IsDanger ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    [ObservableProperty]
    private bool _isOptimized;

    partial void OnLastChangeTextChanged(string value) => OnPropertyChanged(nameof(LastChangeVisibility));
    partial void OnIsDangerChanged(bool value) =>
        OnPropertyChanged(nameof(DangerVisibility));

    public virtual void RefreshState() { }
}

public partial class RegistryTweakViewModel : TweakItemViewModel
{
    public Models.RegistryTweakDefinition Definition { get; }

    public string DefaultValueText { get; private set; } = "";
    public string OptimizedValueText { get; private set; } = "";
    public string OptimizationStatusText { get; private set; } = "";

    public RegistryTweakViewModel(Models.RegistryTweakDefinition def)
    {
        Definition = def;
        DisplayName = def.DisplayName;
        DisplayNameMain = def.DisplayName;
        BlockedSuffix = string.Empty;
        Description = string.IsNullOrWhiteSpace(def.ImpactDescription)
            ? def.Rationale
            : $"{def.Rationale}\n\nAdvertencia: {def.ImpactDescription}";
        IsDanger = def.IsDanger;
        DangerTooltip = string.IsNullOrWhiteSpace(def.ImpactDescription)
            ? "Ajuste sensible al sistema o al hardware: revisa la descripción antes de aplicar."
            : def.ImpactDescription;
        RefreshState();
    }

    public override void RefreshState()
    {
        try
        {
            using var root = Services.RegistryService.GetRootKey(Definition.Hive);
            if (root == null)
            {
                CurrentStateText = "Formato de clave invalido";
                IsPresent = false;
                IsSelectable = false;
                return;
            }

            var expectedKind = Services.RegistryService.ParseValueKind(Definition.ValueKind);
            var expectedValueObj = Services.RegistryService.ConvertValue(Definition.ValueData, expectedKind);
            OptimizedValueText = Services.RegistryValueFormatter.ToDisplayString(expectedValueObj);

            if (Definition.SubKey.EndsWith(@"Tcpip\Parameters\Interfaces", System.StringComparison.OrdinalIgnoreCase))
            {
                // Especial TCP 
                using var interfaceKey = root.OpenSubKey(Definition.SubKey, false);
                if (interfaceKey != null)
                {
                    bool anyOptimized = false;
                    string? firstFound = null;
                    foreach (var name in interfaceKey.GetSubKeyNames())
                    {
                        using var sub = interfaceKey.OpenSubKey(name, false);
                        var v = sub?.GetValue(Definition.ValueName);
                        if (v != null)
                        {
                            var currentInterfaceVal = Services.RegistryValueFormatter.ToDisplayString(v);
                            firstFound ??= currentInterfaceVal;
                            if (Services.RegistryValueComparer.AreEquivalent(v, expectedValueObj, expectedKind))
                            {
                                anyOptimized = true;
                            }
                        }
                    }
                    IsOptimized = anyOptimized;
                    IsPresent = firstFound != null;
                    IsSelectable = true;
                    OptimizationStatusText = IsOptimized ? "optimizado" : "no optimizado";
                    CurrentStateText = firstFound is null
                        ? "Estado actual: inexistente"
                        : $"Estado actual: {OptimizationStatusText} ({firstFound})";
                }
                else
                {
                    IsPresent = false;
                    IsSelectable = true; // se puede crear al aplicar (en interfaces se aplica a subclaves existentes)
                    IsOptimized = false;
                    OptimizationStatusText = "inexistente";
                    CurrentStateText = "Estado actual: inexistente";
                }
                return;
            }

            using var probe = root.OpenSubKey(Definition.SubKey, false);
            var currentVal = probe?.GetValue(Definition.ValueName);
            if (currentVal == null)
            {
                IsPresent = probe != null; // clave puede existir aunque el valor no
                IsSelectable = true;
                IsOptimized = false;
                OptimizationStatusText = probe == null ? "inexistente" : "default";
                DefaultValueText = Services.RegistryDefaults.TryGetDefaultDisplayValue(Definition, out var defTxt) ? defTxt : "";
                CurrentStateText = probe == null
                    ? "Estado actual: inexistente"
                    : (string.IsNullOrWhiteSpace(DefaultValueText) ? "Estado actual: default" : $"Estado actual: default ({DefaultValueText})");
            }
            else
            {
                var currentStr = Services.RegistryValueFormatter.ToDisplayString(currentVal);
                IsOptimized = Services.RegistryValueComparer.AreEquivalent(currentVal, expectedValueObj, expectedKind);
                IsPresent = true;
                IsSelectable = true;

                DefaultValueText = Services.RegistryDefaults.TryGetDefaultDisplayValue(Definition, out var defTxt) ? defTxt : "";

                if (IsOptimized)
                {
                    OptimizationStatusText = Services.RegistryDefaults.IsWindowsDefaultOptimized(Definition, expectedValueObj, expectedKind)
                        ? "optimizado por Windows"
                        : "optimizado";
                    CurrentStateText = $"Estado actual: {OptimizationStatusText} ({currentStr})";
                }
                else
                {
                    // Si coincide con default conocido lo marcamos como default, si no, no optimizado.
                    OptimizationStatusText = Services.RegistryDefaults.IsEquivalentToDefault(Definition, currentVal, expectedKind)
                        ? "default"
                        : "no optimizado";

                    CurrentStateText = (OptimizationStatusText == "default" && !string.IsNullOrWhiteSpace(DefaultValueText))
                        ? $"Estado actual: default ({DefaultValueText})"
                        : $"Estado actual: no optimizado ({currentStr})";
                }
            }
        }
        catch
        {
            CurrentStateText = "[ACTUAL: Error de lectura]";
            IsPresent = true;
            IsSelectable = true;
            IsOptimized = false;
        }
    }
}

public partial class ServiceTweakModel : TweakItemViewModel
{
    public Models.ServiceDefinition Definition { get; }
    public bool IsWifiLocked { get; }
    public bool IsToggleEnabled => !IsWifiLocked;
    public Microsoft.UI.Xaml.Visibility IsWifiLockedVisibility => IsWifiLocked ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public ServiceTweakModel(Models.ServiceDefinition def, bool locked)
    {
        Definition = def;
        IsWifiLocked = locked;
        DisplayName = def.DisplayLabel;
        DisplayNameMain = def.DisplayLabel;
        BlockedSuffix = string.Empty;
        Description = def.UserDescription;
        RefreshState();
    }

    public override void RefreshState()
    {
        var status = Services.SystemStateMonitor.GetServiceStatusInfo(Definition.ServiceName);
        IsPresent = status.Exists;
        IsSelectable = status.Exists && !IsWifiLocked;

        if (!status.Exists)
        {
            IsOptimized = false;
            CurrentStateText = "Estado actual: ⚪ No encontrado";
            IsSelected = false;
            return;
        }

        IsOptimized = status.IsDisabled && status.IsStopped;
        var runTxt = status.IsRunning ? "🟢 Ejecutándose" : "🔴 Detenido";
        var startTxt = status.StartTypeText;
        CurrentStateText = $"Estado actual: {runTxt} | {startTxt}";
    }
}

public partial class CleanerTweakModel : TweakItemViewModel
{
    public Models.CleanerTaskDefinition Definition { get; }

    public CleanerTweakModel(Models.CleanerTaskDefinition def)
    {
        Definition = def;
        DisplayName = def.Id == "recycle-bin" ? "Vaciar Papelera de Reciclaje" : def.DisplayLabel;
        DisplayNameMain = DisplayName;
        BlockedSuffix = string.Empty;
        Description = def.Description;
        IsSelected = def.DefaultRecommended; // Defaults: true for safe ones, false for User Folders
        base.IsDanger = Definition.IsDanger || Definition.Id == "user-folders";
        if (base.IsDanger)
            DangerTooltip = "Eliminación permanente o muy agresiva. Revisa la descripción y haz copia de seguridad si aplica.";
        RefreshState();
    }

    public override void RefreshState()
    {
        if (Definition.Id == "recycle-bin")
        {
            CurrentStateText = string.Empty;
            IsOptimized = false;
            IsPresent = true;
            IsSelectable = true;
            return;
        }

        var status = Services.CleanerOptimization.GetTaskState(Definition.Id);

        var showStatus = Definition.Id is "user-folders" or "temp-files" or "prefetch" or "wu-cache" or "event-logs"
            or "thumbcache" or "delivery-opt" or "icon-cache" or "d3d-shader-cache" or "wer-reports" or "inet-cache";
        CurrentStateText = showStatus ? $"Estado actual: {status}" : string.Empty;

        IsOptimized = status.Contains("vacía", System.StringComparison.OrdinalIgnoreCase)
            || status.Contains("vacÃ­a", System.StringComparison.OrdinalIgnoreCase)
            || status.Contains("limpio", System.StringComparison.OrdinalIgnoreCase)
            || status.Contains("optimizados", System.StringComparison.OrdinalIgnoreCase)
            || status.Contains("VacÃ­as", System.StringComparison.OrdinalIgnoreCase)
            || status.Contains("0 archivos", System.StringComparison.OrdinalIgnoreCase)
            || status.Contains("Sin miniaturas", System.StringComparison.OrdinalIgnoreCase);
        IsPresent = true;
        IsSelectable = true;
    }
}

public partial class ProgramPackageViewModel : TweakItemViewModel
{
    public string PackageId { get; }

    public ProgramPackageViewModel(string displayName, string packageId, string description)
    {
        DisplayName = displayName;
        DisplayNameMain = displayName;
        BlockedSuffix = string.Empty;
        PackageId = packageId;
        Description = description;
        CurrentStateText = $"Id winget: {packageId}";
    }
}

public sealed partial class NetworkQuickActionViewModel : TweakItemViewModel
{
    public Models.NetworkQuickActionDefinition Definition { get; }

    public NetworkQuickActionViewModel(Models.NetworkQuickActionDefinition def)
    {
        Definition = def;
        DisplayName = def.DisplayLabel;
        DisplayNameMain = def.DisplayLabel;
        Description = def.Description;
        IsDanger = def.IsDanger;
        DangerTooltip = string.IsNullOrWhiteSpace(def.ImpactDescription)
            ? def.Description
            : def.ImpactDescription;
        RefreshState();
    }

    public override void RefreshState()
    {
        CurrentStateText = "Acción de sistema (DNS / caché). Ejecutar como administrador si falla el cambio de DNS.";
        IsPresent = true;
        IsSelectable = true;
        IsOptimized = false;
    }
}
