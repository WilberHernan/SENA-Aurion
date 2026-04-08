using SenaAurion.Models;
using Microsoft.UI.Xaml.Media.Imaging;
using System.IO;

namespace SenaAurion.ViewModels;

public sealed partial class InstalledProgramViewModel : TweakItemViewModel
{
    public InstalledProgramInfo Info { get; }

    public InstalledProgramViewModel(InstalledProgramInfo info)
    {
        Info = info;
        DisplayName = info.DisplayName;
        DisplayNameMain = info.DisplayName;
        BlockedSuffix = string.Empty;
        Description = $"{(string.IsNullOrWhiteSpace(info.Publisher) ? "Editor desconocido" : info.Publisher)} · {(string.IsNullOrWhiteSpace(info.DisplayVersion) ? "Versión desconocida" : info.DisplayVersion)}";
        IconSource = TryBuildIcon(info.DisplayIcon);
        RefreshState();
    }

    public override void RefreshState()
    {
        // Para este módulo, el “estado” principal es que el desinstalador exista y sea invocable.
        IsPresent = true;
        IsSelectable = !Info.IsSystemComponent && !string.IsNullOrWhiteSpace(Info.UninstallString);
        IsOptimized = false;

        if (Info.IsSystemComponent)
        {
            CurrentStateText = "Estado actual: Componente del sistema (bloqueado)";
            DisplayNameMain = Info.DisplayName;
            BlockedSuffix = " (bloqueado)";
            IsSelected = false;
            return;
        }

        if (string.IsNullOrWhiteSpace(Info.UninstallString))
        {
            CurrentStateText = "Estado actual: Sin desinstalador registrado";
            IsSelected = false;
            return;
        }

        CurrentStateText = "Estado actual: Listo para desinstalar";
    }

    private static BitmapImage? TryBuildIcon(string rawDisplayIcon)
    {
        if (string.IsNullOrWhiteSpace(rawDisplayIcon)) return null;

        // Common formats: "C:\Path\App.exe,0" or quoted.
        var s = rawDisplayIcon.Trim().Trim('"');
        var comma = s.IndexOf(',');
        if (comma > 0) s = s[..comma];
        s = s.Trim().Trim('"');

        if (string.IsNullOrWhiteSpace(s)) return null;
        if (!Path.IsPathRooted(s)) return null;
        if (!File.Exists(s)) return null;

        try
        {
            return new BitmapImage(new Uri(s));
        }
        catch
        {
            try
            {
                return new BitmapImage(new Uri("file:///" + s.Replace("\\", "/")));
            }
            catch
            {
                return null;
            }
        }
    }
}

