using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using VisionaryOptimizer.Models;

namespace VisionaryOptimizer.ViewModels;

/// <summary>Toggle de deshabilitación por servicio con bloqueo Wi‑Fi.</summary>
public sealed partial class ServiceToggleViewModel : ObservableObject
{
    public ServiceDefinition Definition { get; }

    public string ServiceId => Definition.Id;

    [ObservableProperty]
    private bool _isDisableRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsToggleEnabled))]
    [NotifyPropertyChangedFor(nameof(IsWifiLockedVisibility))]
    private bool _isWifiLocked;

    public bool IsToggleEnabled => !IsWifiLocked;

    public Visibility IsWifiLockedVisibility => IsWifiLocked ? Visibility.Visible : Visibility.Collapsed;

    public ServiceToggleViewModel(ServiceDefinition definition, bool defaultDisable, bool wifiLocked)
    {
        Definition = definition;
        _isWifiLocked = wifiLocked;
        _isDisableRequested = wifiLocked ? false : defaultDisable;
    }

    partial void OnIsWifiLockedChanged(bool value)
    {
        if (value)
            IsDisableRequested = false;
    }
}
