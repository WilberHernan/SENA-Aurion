using Microsoft.UI.Xaml;

namespace VisionaryOptimizer;

public partial class App : Application
{
    private Window m_window = null!;

    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += App_UnhandledException;
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"CRITICAL ERROR: {e.Message}");
        System.Diagnostics.Debug.WriteLine($"STACK TRACE: {e.Exception.StackTrace}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try 
        {
            m_window = new MainWindow();
            m_window.Activate();
        }
        catch (System.Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"INIT ERROR: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"STACK TRACE: {ex.StackTrace}");
            throw;
        }
    }
}
