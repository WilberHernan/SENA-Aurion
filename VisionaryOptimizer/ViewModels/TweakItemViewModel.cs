using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;

namespace VisionaryOptimizer.ViewModels;

public partial class TweakItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _currentStateText = string.Empty;

    [ObservableProperty]
    private bool _isOptimized;

    public virtual void RefreshState() { }
}

public partial class RegistryTweakViewModel : TweakItemViewModel
{
    public Models.RegistryTweakDefinition Definition { get; }

    public RegistryTweakViewModel(Models.RegistryTweakDefinition def)
    {
        Definition = def;
        DisplayName = def.DisplayName;
        Description = def.Rationale;
        RefreshState();
    }

    public override void RefreshState()
    {
        try
        {
            using var root = Services.RegistryService.GetRootKey(Definition.Hive);
            if (root == null)
            {
                CurrentStateText = "Formato de clave inválido";
                return;
            }

            var expectedKind = Services.RegistryService.ParseValueKind(Definition.ValueKind);
            var expectedValueObj = Services.RegistryService.ConvertValue(Definition.ValueData, expectedKind);
            var expectedStr = expectedValueObj?.ToString() ?? "";

            if (Definition.SubKey.EndsWith(@"Tcpip\Parameters\Interfaces", System.StringComparison.OrdinalIgnoreCase))
            {
                // Especial TCP 
                using var interfaceKey = root.OpenSubKey(Definition.SubKey, false);
                if (interfaceKey != null)
                {
                    bool anyOptimized = false;
                    string displayVal = "0 (Original)";
                    foreach (var name in interfaceKey.GetSubKeyNames())
                    {
                        using var sub = interfaceKey.OpenSubKey(name, false);
                        var v = sub?.GetValue(Definition.ValueName);
                        if (v != null)
                        {
                            var currentInterfaceVal = v.ToString() ?? "";
                            if (!anyOptimized) displayVal = currentInterfaceVal;
                            
                            if (currentInterfaceVal == expectedStr) 
                            {
                                anyOptimized = true;
                                displayVal = currentInterfaceVal; // Force the optimized one for the UI showing if at least one hit
                            }
                        }
                    }
                    IsOptimized = anyOptimized;
                    CurrentStateText = $"[ACTUAL: {displayVal} {(IsOptimized ? "✓ optimizado" : "⚠️ original")}]";
                }
                else
                {
                    CurrentStateText = "[ACTUAL: No existe]";
                }
                return;
            }

            using var probe = root.OpenSubKey(Definition.SubKey, false);
            var currentVal = probe?.GetValue(Definition.ValueName);
            if (currentVal == null)
            {
                CurrentStateText = "[ACTUAL: No aplicado ⚠️ original]";
                IsOptimized = false;
            }
            else
            {
                var currentStr = currentVal.ToString() ?? "";
                
                // Si el expectedKind es MultiString, currentVal suele ser string[], así que lo formateamos si es necesario o comparamos distinto
                if (expectedKind == Microsoft.Win32.RegistryValueKind.MultiString)
                {
                    if (currentVal is string[] currentArr && expectedValueObj is string[] expectedArr)
                    {
                        IsOptimized = System.Linq.Enumerable.SequenceEqual(currentArr, expectedArr);
                        currentStr = string.Join(",", currentArr);
                    }
                }
                else
                {
                    IsOptimized = currentStr.Equals(expectedStr, System.StringComparison.OrdinalIgnoreCase);
                }

                CurrentStateText = $"[ACTUAL: {currentStr} {(IsOptimized ? "✓ optimizado" : "⚠️ original")}]";
            }
        }
        catch
        {
            CurrentStateText = "[ACTUAL: Error de lectura]";
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
        Description = def.UserDescription;
        RefreshState();
    }

    public override void RefreshState()
    {
        var rawStatus = Services.SystemStateMonitor.GetServiceStatus(Definition.ServiceName);
        IsOptimized = rawStatus.Contains("Detenido") && rawStatus.Contains("Deshabilitado");
        
        CurrentStateText = $"[ESTADO: {rawStatus} {(IsOptimized ? "✓ optimizado" : "")}]";
    }
}

public partial class CleanerTweakModel : TweakItemViewModel
{
    public Models.CleanerTaskDefinition Definition { get; }

    public bool IsDanger => Definition.IsDanger || Definition.Id == "user-folders";
    public Microsoft.UI.Xaml.Visibility IsDangerVisibility => IsDanger ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public CleanerTweakModel(Models.CleanerTaskDefinition def)
    {
        Definition = def;
        DisplayName = def.Id == "recycle-bin" ? "Vaciar Papelera de Reciclaje" : def.DisplayLabel;
        Description = def.Description;
        IsSelected = def.DefaultRecommended; // Defaults: true for safe ones, false for User Folders
        RefreshState();
    }

    public override void RefreshState()
    {
        if (Definition.Id == "recycle-bin")
        {
            CurrentStateText = string.Empty;
            IsOptimized = false;
            return;
        }

        var status = Services.CleanerOptimization.GetTaskState(Definition.Id);

        if (Definition.Id == "user-folders")
        {
            CurrentStateText = $"[ACTUAL: {status}]";
        }
        else
        {
            CurrentStateText = string.Empty;
        }

        IsOptimized = status.Contains("vacía", System.StringComparison.OrdinalIgnoreCase) || status.Contains("limpio", System.StringComparison.OrdinalIgnoreCase) || status.Contains("optimizados", System.StringComparison.OrdinalIgnoreCase) || status.Contains("Vacías", System.StringComparison.OrdinalIgnoreCase);
    }
}