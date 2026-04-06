using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using System;

namespace VisionaryOptimizer
{
    public sealed partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            this.InitializeComponent();

            // Set window size
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            appWindow.Resize(new SizeInt32 { Width = 900, Height = 580 });

            // Center window (approximation without complex multi-monitor math)
            if (DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary) is DisplayArea displayArea)
            {
                var workArea = displayArea.WorkArea;
                var x = workArea.X + (workArea.Width - 900) / 2;
                var y = workArea.Y + (workArea.Height - 580) / 2;
                appWindow.Move(new PointInt32 { X = x, Y = y });
            }

            // Custom TitleBar
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            // Start Animation
            this.Activated += SplashWindow_Activated;
        }

        private void SplashWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            this.Activated -= SplashWindow_Activated;
            ScrollAnimation.Begin();
        }

        private void TopButton_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = new MainWindow();
            mainWindow.Activate();
            this.Close();
        }
    }
}