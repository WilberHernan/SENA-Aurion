using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace SenaAurion.Controls;

public enum TrafficLightBulbColor
{
    Red,
    Amber,
    Green
}

public sealed partial class TrafficLightBulb : UserControl
{
    public TrafficLightBulb()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyVisuals();
    }

    public TrafficLightBulbColor Color
    {
        get => (TrafficLightBulbColor)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    public static readonly DependencyProperty ColorProperty =
        DependencyProperty.Register(
            nameof(Color),
            typeof(TrafficLightBulbColor),
            typeof(TrafficLightBulb),
            new PropertyMetadata(TrafficLightBulbColor.Red, OnAnyChanged));

    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    public static readonly DependencyProperty IsOnProperty =
        DependencyProperty.Register(
            nameof(IsOn),
            typeof(bool),
            typeof(TrafficLightBulb),
            new PropertyMetadata(false, OnAnyChanged));

    private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TrafficLightBulb bulb)
        {
            bulb.ApplyVisuals();
        }
    }

    private void ApplyVisuals()
    {
        Windows.UI.Color baseColor;
        Windows.UI.Color darkColor;

        switch (Color)
        {
            case TrafficLightBulbColor.Red:
                baseColor = Windows.UI.Color.FromArgb(255, 235, 59, 59);
                darkColor = Windows.UI.Color.FromArgb(255, 90, 15, 15);
                break;
            case TrafficLightBulbColor.Amber:
                baseColor = Windows.UI.Color.FromArgb(255, 255, 176, 32);
                darkColor = Windows.UI.Color.FromArgb(255, 106, 59, 0);
                break;
            case TrafficLightBulbColor.Green:
                baseColor = Windows.UI.Color.FromArgb(255, 37, 211, 102);
                darkColor = Windows.UI.Color.FromArgb(255, 11, 74, 32);
                break;
            default:
                baseColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                darkColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
                break;
        }

        // Dome gradient colors
        DomeStop1.Color = WithAlpha(baseColor, 0xFF);
        DomeStop2.Color = WithAlpha(baseColor, 0xD6);
        DomeStop3.Color = WithAlpha(darkColor, 0xCC);
        DomeStop4.Color = WithAlpha(darkColor, 0xFF);

        // Border: subtle darker rim
        BulbBorderBrush.Color = WithAlpha(darkColor, 0x80);

        // Glow gradient: transparent center -> colored edge
        GlowStop1.Color = WithAlpha(baseColor, 0x00);
        GlowStop2.Color = WithAlpha(baseColor, 0xCC);

        VisualStateManager.GoToState(this, IsOn ? "On" : "Off", true);
    }

    private static Windows.UI.Color WithAlpha(Windows.UI.Color c, byte a) => Windows.UI.Color.FromArgb(a, c.R, c.G, c.B);
}

