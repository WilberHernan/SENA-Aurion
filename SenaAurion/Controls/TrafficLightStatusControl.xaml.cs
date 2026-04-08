using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SenaAurion.Controls;

public enum TrafficLightState
{
    NoneOptimized,
    SomeOptimized,
    AllOptimized
}

public sealed partial class TrafficLightStatusControl : UserControl
{
    public TrafficLightStatusControl()
    {
        InitializeComponent();
    }

    public TrafficLightState State
    {
        get => (TrafficLightState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(
            nameof(State),
            typeof(TrafficLightState),
            typeof(TrafficLightStatusControl),
            new PropertyMetadata(TrafficLightState.NoneOptimized, OnStateChanged));

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TrafficLightStatusControl ctl)
        {
            ctl.Bindings?.Update();
        }
    }

    public bool IsRedOn => State == TrafficLightState.NoneOptimized;
    public bool IsAmberOn => State == TrafficLightState.SomeOptimized;
    public bool IsGreenOn => State == TrafficLightState.AllOptimized;

    public string RedTooltip => "Ninguna función optimizada";
    public string AmberTooltip => "Algunas funciones optimizadas";
    public string GreenTooltip => "Todas las funciones optimizadas";
}

