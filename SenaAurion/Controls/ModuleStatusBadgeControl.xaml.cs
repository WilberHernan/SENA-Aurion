using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SenaAurion.Controls;
using Windows.UI;

namespace SenaAurion.Controls;

public sealed partial class ModuleStatusBadgeControl : UserControl
{
    public ModuleStatusBadgeControl()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyVisuals();
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
            typeof(ModuleStatusBadgeControl),
            new PropertyMetadata(TrafficLightState.NoneOptimized, OnAnyChanged));

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(ModuleStatusBadgeControl),
            new PropertyMetadata(string.Empty, OnAnyChanged));

    private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ModuleStatusBadgeControl ctl)
            ctl.ApplyVisuals();
    }

    private void ApplyVisuals()
    {
        var (dot, glow) = State switch
        {
            TrafficLightState.NoneOptimized => (Color.FromArgb(255, 191, 91, 91), Color.FromArgb(255, 191, 91, 91)),
            TrafficLightState.SomeOptimized => (Color.FromArgb(255, 201, 167, 96), Color.FromArgb(255, 201, 167, 96)),
            TrafficLightState.AllOptimized => (Color.FromArgb(255, 109, 196, 145), Color.FromArgb(255, 109, 196, 145)),
            _ => (Color.FromArgb(255, 150, 150, 150), Color.FromArgb(255, 150, 150, 150))
        };

        Dot.Fill = new SolidColorBrush(dot);
        GlowA.Color = Color.FromArgb(0, glow.R, glow.G, glow.B);
        GlowB.Color = Color.FromArgb(170, glow.R, glow.G, glow.B);
        DotGlow.Opacity = 0.55;
    }
}

