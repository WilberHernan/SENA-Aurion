using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SenaAurion.Models;
using SenaAurion.Services;

namespace SenaAurion.ViewModels;

/// <summary>Shell MVVM: navegaciÃ³n lateral, datos offline y motor de optimizaciÃ³n async.</summary>
public sealed partial class MainViewModel : ViewModelBase
{
    private readonly IOptimizationDataProvider _dataProvider;
    private readonly IOptimizationEngine _engine;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;

    public MainViewModel(IOptimizationDataProvider dataProvider, IOptimizationEngine engine)
    {
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _dataProvider = dataProvider;
        _engine = engine;
        _ = LoadDataAsync();

        SystemStateMonitor.StateChanged += (s, e) => 
        {
            if (_dispatcher != null)
            {
                _dispatcher.TryEnqueue(() => RefreshStates());
            }
            else
            {
                RefreshStates();
            }
        };
    }

    public void RefreshStates()
    {
        InputStateText = SystemStateMonitor.GetInputState();
        NetworkStateText = SystemStateMonitor.GetNetworkState();

        foreach (var item in InputTweakItems) item.RefreshState();
        foreach (var item in NetworkTweakItems) item.RefreshState();
        foreach (var item in ServiceTweakItems) item.RefreshState();
        foreach (var item in CleanerTweakItems) item.RefreshState();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RegistryTweakCount))]
    [NotifyPropertyChangedFor(nameof(ServiceCount))]
    [NotifyPropertyChangedFor(nameof(InputTweaks))]
    [NotifyPropertyChangedFor(nameof(NetworkTcpTweaks))]
    [NotifyPropertyChangedFor(nameof(WifiCriticalServices))]
    [NotifyPropertyChangedFor(nameof(ServiceItems))]
    private OptimizationDataDocument? _data;

    public int RegistryTweakCount => Data?.RegistryTweaks?.Count ?? 0;

    public int ServiceCount => Data?.Services?.Count ?? 0;

    public IReadOnlyList<RegistryTweakDefinition> InputTweaks =>
        Data?.InputLatency?.Tweaks?.ToArray() ?? Array.Empty<RegistryTweakDefinition>();

    public IReadOnlyList<RegistryTweakDefinition> NetworkTcpTweaks =>
        Data?.NetworkTcp?.Tweaks?.ToArray() ?? Array.Empty<RegistryTweakDefinition>();

    public IReadOnlyList<string> WifiCriticalServices =>
        Data?.NetworkTcp?.WifiCriticalServices?.ToArray() ?? Array.Empty<string>();

    public IReadOnlyList<ServiceDefinition> ServiceItems =>
        Data?.Services?.ToArray() ?? Array.Empty<ServiceDefinition>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSectionHome))]
    [NotifyPropertyChangedFor(nameof(IsSectionInput))]
    [NotifyPropertyChangedFor(nameof(IsSectionNetwork))]
    [NotifyPropertyChangedFor(nameof(IsSectionServices))]
    [NotifyPropertyChangedFor(nameof(IsSectionCleaner))]
    private string _selectedTag = "home";

    public bool IsSectionHome => SelectedTag == "home";

    public bool IsSectionInput => SelectedTag == "input";

    public bool IsSectionNetwork => SelectedTag == "network";

    public bool IsSectionServices => SelectedTag == "services";

    public bool IsSectionCleaner => SelectedTag == "cleaner";

    // SECCIÃ“N DE ESTADO GENERAL Y OPCIONES
    [ObservableProperty]
    private string _statusMessage = "Listo Â· modo offline";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WifiStatusText))]
    private bool _isWifiPrimary;

    public string WifiStatusText => IsWifiPrimary ? "sÃ­" : "no";

    [ObservableProperty]
    private bool _isBusy;

    // INCLUSIONES DE MÃ“DULOS (INICIO)
    [ObservableProperty]
    private bool _includeInputRegistry = true;

    [ObservableProperty]
    private bool _includeNetworkTcp = true;

    [ObservableProperty]
    private bool _includeRegistryMisc = true;

    [ObservableProperty]
    private bool _includeServices = true;

    // SELECCIÃ“N "TODOS" POR MÃ“DULOS
    [ObservableProperty]
    private bool _selectAllInput;
    partial void OnSelectAllInputChanged(bool value)
    {
        foreach (var item in InputTweakItems) item.IsSelected = value;
    }

    [ObservableProperty]
    private bool _selectAllNetwork;
    partial void OnSelectAllNetworkChanged(bool value)
    {
        foreach (var item in NetworkTweakItems) item.IsSelected = value;
    }

    [ObservableProperty]
    private bool _selectAllServices;
    partial void OnSelectAllServicesChanged(bool value)
    {
        foreach (var item in ServiceTweakItems) if (!item.IsWifiLocked) item.IsSelected = value;
    }

    [ObservableProperty]
    private bool _selectAllCleaner;
    partial void OnSelectAllCleanerChanged(bool value)
    {
        foreach (var item in CleanerTweakItems) 
        {
            if (value && item.IsDanger)
                item.IsSelected = false; // Prevents "Select All" from indiscriminately destroying user's personal documents sin manual check.
            else
                item.IsSelected = value;
        }
    }

    // ESTADOS EN TIEMPO REAL
    [ObservableProperty]
    private string _inputStateText = "Cargando...";

    [ObservableProperty]
    private string _networkStateText = "Cargando...";

    [ObservableProperty]
    private string _cleanerStateText = "Memoria no calculada";

    // COLECCIONES UI
    public ObservableCollection<ServiceToggleViewModel> ServiceToggles { get; } = new();
    public ObservableCollection<RegistryTweakViewModel> InputTweakItems { get; } = new();
    public ObservableCollection<RegistryTweakViewModel> NetworkTweakItems { get; } = new();
    public ObservableCollection<ServiceTweakModel> ServiceTweakItems { get; } = new();
    public ObservableCollection<CleanerTweakModel> CleanerTweakItems { get; } = new();
    public ObservableCollection<TweakItemViewModel> CurrentModuleItems { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentModuleDescription))]
    [NotifyPropertyChangedFor(nameof(SelectedModuleDisplayName))]
    [NotifyPropertyChangedFor(nameof(SelectedModuleDetail))]
    private TweakItemViewModel? _selectedModuleItem;

    // SYSTEM INFO HOME
    [ObservableProperty]
    private SystemInfoModel? _systemInfo;

    public IReadOnlyList<NavItem> NavItems { get; } =
    [
        new("home", "Inicio", "\uE80F"),
        new("input", "Input Response", "\uE144"),
        new("network", "Red inteligente", "\uE701"),
        new("services", "Servicios y telemetrÃ­a", "\uE9D9"),
        new("cleaner", "Limpieza Profunda", "\uE7FC")
    ];

    partial void OnSelectedTagChanged(string value)
    {
        StatusMessage = value switch
        {
            "home" => "VisiÃ³n general Â· Visionary Optimizer",
            "input" => "Teclado, ratÃ³n y menÃºs Â· baja latencia",
            "network" => "TCP/IP Â· detecciÃ³n Wi-Fi activa",
            "services" => "Control granular de servicios",
            "cleaner" => "Mantenimiento y recuperaciÃ³n de espacio",
            _ => StatusMessage,
        };
        OnPropertyChanged(nameof(IsSectionHome));
        OnPropertyChanged(nameof(IsSectionInput));
        OnPropertyChanged(nameof(IsSectionNetwork));
        OnPropertyChanged(nameof(IsSectionServices));
        OnPropertyChanged(nameof(IsSectionCleaner));

        if (value == "home")
        {
            _ = LoadSystemInfoAsync();
        }

        RefreshCurrentModuleView();
    }

    public string CurrentModuleTitle => SelectedTag switch
    {
        "input" => "Input",
        "network" => "Network",
        "services" => "Services",
        "cleaner" => "Cleaner",
        _ => "Inicio"
    };

    public string CurrentModuleActionName => SelectedTag switch
    {
        "input" => "Input",
        "network" => "Network",
        "services" => "Services",
        "cleaner" => "Cleaner",
        _ => "Input"
    };

    public string CurrentModuleDescription => SelectedTag switch
    {
        "input" => "Ajustes de latencia para periféricos y respuesta del sistema.",
        "network" => "Tweaks TCP/IP enfocados en estabilidad y menor latencia.",
        "services" => "Gestión de servicios para reducir carga sin dañar el sistema.",
        "cleaner" => "Tareas de limpieza y mantenimiento de archivos temporales.",
        _ => "Panel principal de estado y resumen del equipo."
    };

    public string SelectedModuleDisplayName => SelectedModuleItem?.DisplayName ?? "Selecciona una función";
    public string SelectedModuleDetail => SelectedModuleItem?.Description ?? CurrentModuleDescription;

    private void RefreshCurrentModuleView()
    {
        CurrentModuleItems.Clear();
        IEnumerable<TweakItemViewModel> source = SelectedTag switch
        {
            "input" => InputTweakItems,
            "network" => NetworkTweakItems,
            "services" => ServiceTweakItems,
            "cleaner" => CleanerTweakItems,
            _ => Array.Empty<TweakItemViewModel>()
        };

        foreach (var item in source)
            CurrentModuleItems.Add(item);

        SelectedModuleItem = CurrentModuleItems.FirstOrDefault();

        OnPropertyChanged(nameof(CurrentModuleTitle));
        OnPropertyChanged(nameof(CurrentModuleActionName));
        OnPropertyChanged(nameof(CurrentModuleDescription));
    }

    private async Task LoadSystemInfoAsync()
    {
        SystemInfo = await SystemInfoService.GetSystemInfoAsync();
    }

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        try
        {
            Data = await _dataProvider.LoadAsync().ConfigureAwait(true);
            StatusMessage = Data is null ? "No se pudo cargar OptimizationData.json" : "Datos cargados (offline)";
            RefreshNetworkAndToggles();
            _ = LoadSystemInfoAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al cargar datos: {ex.Message}";
        }
    }

    public void RefreshNetworkAndToggles()
    {
        IsWifiPrimary = NetworkHardwareDetector.IsWirelessAdapterActive();
        RebuildServiceToggles();
        RefreshStates();
    }

    private void RebuildServiceToggles()
    {
        ServiceToggles.Clear();
        ServiceTweakItems.Clear();
        InputTweakItems.Clear();
        NetworkTweakItems.Clear();
        CleanerTweakItems.Clear();

        if (Data is null)
            return;

        var wifi = NetworkHardwareDetector.IsWirelessAdapterActive();
        var critical = new HashSet<string>(
            Data.NetworkTcp.WifiCriticalServices.Select(s => s.Trim()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var s in Data.Services)
        {
            var locked = wifi && (critical.Contains(s.ServiceName) || s.NeverDisableWhenWifi);
            ServiceToggles.Add(new ServiceToggleViewModel(s, s.DefaultRecommended, locked));
            ServiceTweakItems.Add(new ServiceTweakModel(s, locked) { IsSelected = s.DefaultRecommended });
        }

        foreach (var t in Data.InputLatency.Tweaks) InputTweakItems.Add(new RegistryTweakViewModel(t));
        foreach (var t in Data.NetworkTcp.Tweaks) NetworkTweakItems.Add(new RegistryTweakViewModel(t));
        
        if (Data.Cleaner?.Tasks != null)
        {
            foreach (var task in Data.Cleaner.Tasks)
            {
                var model = new CleanerTweakModel(task);
                if (task.Id == "user-folders")
                {
                    model.DisplayName = "CARPETAS DE USUARIO"; 
                }
                CleanerTweakItems.Add(model);
            }
        }

        SelectAllInput = false;
        SelectAllNetwork = false;
        SelectAllCleaner = false;
        // ... Check if we want to default false? Yes.
        RefreshCurrentModuleView();
    }

    [RelayCommand]
    private async Task RunOptimizationAsync(CancellationToken cancellationToken)
    {
        if (Data is null)
        {
            StatusMessage = "Sin datos Â· no se ejecuta";
            return;
        }

        IsBusy = true;
        StatusMessage = "Aplicando optimizaciÃ³nâ€¦";
        try
        {
            var selections = ServiceToggles.ToDictionary(
                t => t.ServiceId,
                t => t.IsDisableRequested,
                StringComparer.OrdinalIgnoreCase);

            var options = new OptimizationRunOptions
            {
                ApplyInput = IncludeInputRegistry,
                ApplyNetworkTcp = IncludeNetworkTcp,
                ApplyRegistryMisc = IncludeRegistryMisc,
                ApplyServices = IncludeServices,
                ServiceDisableSelections = selections,
            };

            await _engine.RunAsync(Data, options, cancellationToken).ConfigureAwait(true);
            StatusMessage = "OptimizaciÃ³n completada Â· ver SenaAurion.log";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Cancelado";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyModuleAsync(string moduleName, CancellationToken cancellationToken)
    {
        if (Data is null) return;
        IsBusy = true;
        StatusMessage = $"Aplicando {moduleName}â€¦";
        try
        {
            var selections = ServiceToggles.ToDictionary(
                t => t.ServiceId,
                t => t.IsDisableRequested,
                StringComparer.OrdinalIgnoreCase);

            var options = new OptimizationRunOptions
            {
                ApplyInput = moduleName == "Input",
                ApplyNetworkTcp = moduleName == "Network",
                ApplyServices = moduleName == "Services",
                ApplyRegistryMisc = false,
                ServiceDisableSelections = selections
            };

            await _engine.RunAsync(Data, options, cancellationToken).ConfigureAwait(true);
            StatusMessage = $"{moduleName} optimizado.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplySelectedModuleAsync(string moduleName, CancellationToken cancellationToken)
    {
        if (Data is null) return;
        IsBusy = true;
        StatusMessage = $"Aplicando seleccionados en {moduleName}â€¦";
        try
        {
            if (moduleName == "Input")
                await _engine.ApplyTweaksAsync(InputTweakItems.Where(t => t.IsSelected).Select(t => t.Definition), cancellationToken);
            else if (moduleName == "Network")
                await _engine.ApplyTweaksAsync(NetworkTweakItems.Where(t => t.IsSelected).Select(t => t.Definition), cancellationToken);
            else if (moduleName == "Services")
                await _engine.ApplyServicesAsync(ServiceTweakItems.Where(t => t.IsSelected).Select(t => t.Definition), cancellationToken);
            else if (moduleName == "Cleaner")
                await _engine.ApplyCleanerAsync(CleanerTweakItems.Where(t => t.IsSelected).Select(t => t.Definition), cancellationToken);

            StatusMessage = $"{moduleName} optimizado (selecciÃ³n).";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RevertSelectedModuleAsync(string moduleName, CancellationToken cancellationToken)
    {
        if (Data is null) return;
        IsBusy = true;
        StatusMessage = $"Revirtiendo seleccionados en {moduleName}â€¦";
        try
        {
            if (moduleName == "Input")
                await _engine.RevertInputTweaksAsync(InputTweakItems.Where(t => t.IsSelected).Select(t => t.Definition), cancellationToken);
            else if (moduleName == "Network")
                await _engine.RevertNetworkTweaksAsync(NetworkTweakItems.Where(t => t.IsSelected).Select(t => t.Definition), cancellationToken);
            else if (moduleName == "Services")
                await _engine.RevertServicesAsync(ServiceTweakItems.Where(t => t.IsSelected).Select(t => t.Definition), cancellationToken);
            else if (moduleName == "Cleaner")
                await _engine.RevertCleanerAsync(CleanerTweakItems.Where(t => t.IsSelected).Select(t => t.Definition), cancellationToken);

            StatusMessage = $"{moduleName} revertido (selecciÃ³n).";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RevertAllModuleAsync(string moduleName, CancellationToken cancellationToken)
    {
        if (Data is null) return;
        IsBusy = true;
        StatusMessage = $"Revirtiendo todo en {moduleName}â€¦";
        try
        {
            await _engine.RevertModuleAsync(moduleName, Data, cancellationToken);
            StatusMessage = $"{moduleName} revertido a original.";
        }
        catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
        finally { IsBusy = false; }
    }

    public readonly record struct NavItem(string Tag, string Label, string Glyph);
}

