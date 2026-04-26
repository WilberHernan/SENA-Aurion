using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SenaAurion.Models;
using SenaAurion.Services;

namespace SenaAurion.ViewModels;

/// <summary>Shell MVVM: navegación lateral, datos sin conexión y motor de optimización asíncrono.</summary>
public sealed partial class MainViewModel : ViewModelBase
{
    private readonly IOptimizationDataProvider _dataProvider;
    private readonly IOptimizationEngine _engine;
    private readonly WingetProgramService _wingetService;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private CancellationTokenSource? _wingetProbeCts;
    private CancellationTokenSource? _wingetProbeHideCts;
    private string _activeOperationModuleTag = string.Empty;
    private readonly Dictionary<string, string> _moduleResultTextByTag = new(StringComparer.OrdinalIgnoreCase);

    public MainViewModel(IOptimizationDataProvider dataProvider, IOptimizationEngine engine)
    {
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _dataProvider = dataProvider;
        _engine = engine;
        _wingetService = new WingetProgramService();
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
        ServicesStateText = Data?.Services != null
            ? SystemStateMonitor.GetServicesState(Data.Services.Select(s => s.ServiceName))
            : "Sin datos de servicios";

        foreach (var item in InputTweakItems) item.RefreshState();
        foreach (var item in NetworkTweakItems) item.RefreshState();
        foreach (var item in ServiceTweakItems) item.RefreshState();
        foreach (var item in CleanerTweakItems) item.RefreshState();
        OnPropertyChanged(nameof(CurrentModuleSummaryText));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RegistryTweakCount))]
    [NotifyPropertyChangedFor(nameof(ServiceCount))]
    [NotifyPropertyChangedFor(nameof(InputTweaks))]
    [NotifyPropertyChangedFor(nameof(NetworkTcpTweaks))]
    [NotifyPropertyChangedFor(nameof(WifiCriticalServices))]
    [NotifyPropertyChangedFor(nameof(ServiceItems))]
    [NotifyPropertyChangedFor(nameof(ServiceProfileDefinitions))]
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

    public IReadOnlyList<ServiceProfileDefinition> ServiceProfileDefinitions =>
        Data?.ServiceProfiles?.ToArray() ?? Array.Empty<ServiceProfileDefinition>();

    public bool IsServicesModule => SelectedTag == "services";

    public Microsoft.UI.Xaml.Visibility ServiceProfilesVisibility =>
        IsServicesModule ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public bool IsInputModule => SelectedTag == "input";

    public IReadOnlyList<InputProfileDefinition> InputProfileDefinitions =>
        Data?.InputProfiles?.ToArray() ?? Array.Empty<InputProfileDefinition>();

    public Microsoft.UI.Xaml.Visibility InputProfilesVisibility =>
        IsInputModule ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public bool IsNetworkModule => SelectedTag == "network";

    public IReadOnlyList<NetworkProfileDefinition> NetworkProfileDefinitions =>
        Data?.NetworkProfiles?.ToArray() ?? Array.Empty<NetworkProfileDefinition>();

    public Microsoft.UI.Xaml.Visibility NetworkProfilesVisibility =>
        IsNetworkModule ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

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
    private string _statusMessage = "Listo  modo sin conexion";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WifiStatusText))]
    private bool _isWifiPrimary;

    public string WifiStatusText => IsWifiPrimary ? "si­" : "no";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isCurrentModuleApplying;

    [ObservableProperty]
    private bool _isWingetProbeRunning;

    public Microsoft.UI.Xaml.Visibility CurrentModuleIsApplyingVisibility =>
        IsCurrentModuleApplying ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility CurrentModuleProgressVisibility =>
        IsCurrentModuleApplying && !IsWingetProbeRunning
        && string.Equals(SelectedTag, _activeOperationModuleTag, StringComparison.OrdinalIgnoreCase)
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility BackgroundOperationNoticeVisibility =>
        IsCurrentModuleApplying && !IsWingetProbeRunning && !string.IsNullOrWhiteSpace(_activeOperationModuleTag)
        && !string.Equals(SelectedTag, _activeOperationModuleTag, StringComparison.OrdinalIgnoreCase)
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string BackgroundOperationNoticeText =>
        string.IsNullOrWhiteSpace(_activeOperationModuleTag)
            ? string.Empty
            : $"Procesando «{MapModuleTagToLabel(_activeOperationModuleTag)}» en segundo plano…";

    private static string MapModuleTagToLabel(string tag) => tag switch
    {
        "input" => "Entrada",
        "network" => "Red",
        "services" => "Servicios",
        "cleaner" => "Limpieza",
        "programs" => "Programas",
        _ => tag
    };

    private void BeginModuleOperation(string moduleTag)
    {
        _activeOperationModuleTag = moduleTag;
        IsCurrentModuleApplying = true;
        OnPropertyChanged(nameof(CurrentModuleProgressVisibility));
        OnPropertyChanged(nameof(BackgroundOperationNoticeVisibility));
        OnPropertyChanged(nameof(BackgroundOperationNoticeText));
    }

    private void EndModuleOperation()
    {
        IsCurrentModuleApplying = false;
        _activeOperationModuleTag = string.Empty;
        OnPropertyChanged(nameof(CurrentModuleProgressVisibility));
        OnPropertyChanged(nameof(BackgroundOperationNoticeVisibility));
        OnPropertyChanged(nameof(BackgroundOperationNoticeText));
    }

    private static bool IsProgramOperationName(string? moduleTagOrName) =>
        string.Equals(moduleTagOrName, "programs", StringComparison.OrdinalIgnoreCase)
        || string.Equals(moduleTagOrName, "Programas", StringComparison.OrdinalIgnoreCase);

    private void SetModuleResult(string moduleTag, string result)
    {
        if (string.IsNullOrWhiteSpace(moduleTag)) return;
        _moduleResultTextByTag[moduleTag] = result;
        if (string.Equals(SelectedTag, moduleTag, StringComparison.OrdinalIgnoreCase))
            CurrentModuleLastResultText = result;
    }

    private void ClearModuleResult(string moduleTag)
    {
        if (string.IsNullOrWhiteSpace(moduleTag)) return;
        _moduleResultTextByTag.Remove(moduleTag);
        if (string.Equals(SelectedTag, moduleTag, StringComparison.OrdinalIgnoreCase))
            CurrentModuleLastResultText = string.Empty;
    }

    [ObservableProperty]
    private string _currentModuleLastResultText = string.Empty;

    [ObservableProperty]
    private string _wingetProbeText = string.Empty;

    [ObservableProperty]
    private string _currentModuleLastResultLevel = "none";

    public Microsoft.UI.Xaml.Visibility CurrentModuleLastResultVisibility =>
        string.IsNullOrWhiteSpace(CurrentModuleLastResultText)
            ? Microsoft.UI.Xaml.Visibility.Collapsed
            : Microsoft.UI.Xaml.Visibility.Visible;

    public Microsoft.UI.Xaml.Visibility WingetProbeTextVisibility =>
        IsProgramsModule && !string.IsNullOrWhiteSpace(WingetProbeText)
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Media.Brush CurrentModuleLastResultBrush =>
        new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 179, 179, 179));

    public string CurrentModuleSummaryText => ComputeCurrentModuleSummary();

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
        foreach (var item in InputTweakItems)
        {
            if (value && item.IsDanger)
                continue;
            item.IsSelected = value;
        }
    }

    [ObservableProperty]
    private bool _selectAllNetwork;
    partial void OnSelectAllNetworkChanged(bool value)
    {
        foreach (var item in NetworkTweakItems)
        {
            if (value && item.IsDanger)
                continue;
            item.IsSelected = value;
        }

        foreach (var item in NetworkQuickActionItems)
        {
            if (value && item.IsDanger)
                continue;
            item.IsSelected = value;
        }
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

    [ObservableProperty]
    private bool _selectAllPrograms;
    partial void OnSelectAllProgramsChanged(bool value)
    {
        foreach (var item in ProgramPackageItems) item.IsSelected = value;
    }

    // ESTADOS EN TIEMPO REAL
    [ObservableProperty]
    private string _inputStateText = "Cargando...";

    [ObservableProperty]
    private string _networkStateText = "Cargando...";

    [ObservableProperty]
    private string _servicesStateText = "Cargando...";

    [ObservableProperty]
    private string _cleanerStateText = "Memoria no calculada";

    // COLECCIONES UI
    public ObservableCollection<ServiceToggleViewModel> ServiceToggles { get; } = new();
    public ObservableCollection<RegistryTweakViewModel> InputTweakItems { get; } = new();
    public ObservableCollection<RegistryTweakViewModel> NetworkTweakItems { get; } = new();
    public ObservableCollection<NetworkQuickActionViewModel> NetworkQuickActionItems { get; } = new();
    public ObservableCollection<NetworkQuickActionViewModel> NetworkQuickDiagnosticItems { get; } = new();
    public ObservableCollection<NetworkQuickActionViewModel> NetworkQuickTechnicalItems { get; } = new();
    public ObservableCollection<ServiceTweakModel> ServiceTweakItems { get; } = new();
    public ObservableCollection<CleanerTweakModel> CleanerTweakItems { get; } = new();
    public ObservableCollection<ProgramPackageViewModel> ProgramPackageItems { get; } = new();
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
        new("input", "Respuesta de entrada", "\uE144"),
        new("network", "Red inteligente", "\uE701"),
        new("services", "Servicios y telemetría", "\uE9D9"),
        new("cleaner", "Limpieza profunda", "\uE7FC"),
        new("programs", "Gestión de programas", "\uEA37")
    ];

    partial void OnSelectedTagChanged(string value)
    {
        StatusMessage = value switch
        {
            "home" => "Visión general · Optimizador SENA Aurion",
            "input" => "Teclado, ratón y menús · baja latencia",
            "network" => "Red · detección de Wi‑Fi activa",
            "services" => "Gestión de servicios del sistema · perfiles SENA",
            "cleaner" => "Mantenimiento y recuperación de espacio",
            "programs" => "Instalación, actualización y desinstalación con winget",
            _ => StatusMessage,
        };
        OnPropertyChanged(nameof(IsSectionHome));
        OnPropertyChanged(nameof(IsSectionInput));
        OnPropertyChanged(nameof(IsSectionNetwork));
        OnPropertyChanged(nameof(IsSectionServices));
        OnPropertyChanged(nameof(IsSectionCleaner));
        OnPropertyChanged(nameof(IsServicesModule));
        OnPropertyChanged(nameof(ServiceProfilesVisibility));
        OnPropertyChanged(nameof(IsInputModule));
        OnPropertyChanged(nameof(InputProfilesVisibility));
        OnPropertyChanged(nameof(IsNetworkModule));
        OnPropertyChanged(nameof(NetworkProfilesVisibility));
        OnPropertyChanged(nameof(IsCleanerModule));
        OnPropertyChanged(nameof(IsProgramsModule));
        OnPropertyChanged(nameof(WingetProbeTextVisibility));
        OnPropertyChanged(nameof(CurrentModuleProgressVisibility));
        OnPropertyChanged(nameof(BackgroundOperationNoticeVisibility));
        OnPropertyChanged(nameof(BackgroundOperationNoticeText));

        if (value == "home")
        {
            _ = LoadSystemInfoAsync();
        }

        RefreshCurrentModuleView();
        ResetModuleTransientUiOnSwitch();

        if (_moduleResultTextByTag.TryGetValue(value, out var moduleResult))
            CurrentModuleLastResultText = moduleResult;
        else
            CurrentModuleLastResultText = string.Empty;

        if (value == "programs")
        {
            // No iniciar probe si hay operación activa de otro módulo;
            // evita pisar estado de progreso en curso.
            if (!(IsCurrentModuleApplying
                  && !string.IsNullOrWhiteSpace(_activeOperationModuleTag)
                  && !string.Equals(_activeOperationModuleTag, "programs", StringComparison.OrdinalIgnoreCase)))
            {
                _ = AutoProbeWingetOnProgramsEntryAsync();
            }
        }
        else
        {
            CancelWingetProbeFlows();
        }

        OnPropertyChanged(nameof(CurrentModuleSummaryText));
        OnPropertyChanged(nameof(CurrentModuleIsApplyingVisibility));
        OnPropertyChanged(nameof(CurrentModuleLastResultVisibility));
    }

    public string CurrentModuleTitle => SelectedTag switch
    {
        "input" => "Entrada",
        "network" => "Red",
        "services" => "Servicios",
        "cleaner" => "Limpieza",
        "programs" => "Programas",
        _ => "Inicio"
    };

    public string CurrentModuleActionName => SelectedTag switch
    {
        "input" => "input",
        "network" => "network",
        "services" => "services",
        "cleaner" => "cleaner",
        "programs" => "programs",
        _ => "input"
    };

    public string CurrentModuleDescription => SelectedTag switch
    {
        "input" => "Ajusta el registro para mejorar la respuesta del teclado, el ratón y los menús (latencia más baja). Los cambios son reversibles desde la propia aplicación.",
        "network" => "Aplica ajustes TCP/IP para mayor estabilidad y menor latencia en el tráfico de red. El motor respeta servicios críticos cuando hay Wi‑Fi activa.",
        "services" => "Gestiona servicios no esenciales para reducir consumo de recursos y mejorar tiempos de arranque. Incluye perfiles predefinidos para el contexto institucional del SENA (estándar, aulas, oficinas, portátiles). Cada cambio es reversible y se documenta en el log.",
        "cleaner" => "Libera espacio eliminando temporales, cachés y datos prescindibles. Algunas acciones borran archivos de forma permanente; revísalas antes de ejecutar.",
        "programs" => "Instala, actualiza o desinstala programas usando winget (identificadores de paquete). Útil para despliegue y mantenimiento desde soporte técnico.",
        _ => "Resumen del equipo y punto de partida. Desde aquí navega a cada módulo para aplicar optimizaciones concretas con reglas claras y registro de actividad."
    };
    public bool IsCleanerModule => SelectedTag == "cleaner";
    public bool IsProgramsModule => SelectedTag == "programs";

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
            "programs" => ProgramPackageItems,
            _ => Array.Empty<TweakItemViewModel>()
        };

        foreach (var item in source)
            CurrentModuleItems.Add(item);

        SelectedModuleItem = CurrentModuleItems.FirstOrDefault();

        OnPropertyChanged(nameof(CurrentModuleTitle));
        OnPropertyChanged(nameof(CurrentModuleActionName));
        OnPropertyChanged(nameof(CurrentModuleDescription));
        OnPropertyChanged(nameof(CurrentModuleSummaryText));
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
            StatusMessage = Data is null ? "No se pudo cargar OptimizationData.json" : "Datos cargados (sin conexion)";
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
        NetworkQuickActionItems.Clear();
        CleanerTweakItems.Clear();
        ProgramPackageItems.Clear();

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
        NetworkQuickDiagnosticItems.Clear();
        NetworkQuickTechnicalItems.Clear();
        if (Data.NetworkQuickActions is { Count: > 0 })
        {
            foreach (var a in Data.NetworkQuickActions)
            {
                var vm = new NetworkQuickActionViewModel(a);
                NetworkQuickActionItems.Add(vm);
                if (string.Equals(a.Group, "Tecnico", StringComparison.OrdinalIgnoreCase))
                    NetworkQuickTechnicalItems.Add(vm);
                else
                    NetworkQuickDiagnosticItems.Add(vm);
            }
        }
        
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

        // Programas esenciales para técnicos de sistemas
        ProgramPackageItems.Add(new ProgramPackageViewModel("Google Chrome", "Google.Chrome", "Navegador web"));
        ProgramPackageItems.Add(new ProgramPackageViewModel("Mozilla Firefox", "Mozilla.Firefox", "Navegador web"));
        ProgramPackageItems.Add(new ProgramPackageViewModel("7-Zip", "7zip.7zip", "Compresión y extracción"));
        ProgramPackageItems.Add(new ProgramPackageViewModel("Notepad++", "Notepad++.Notepad++", "Editor de texto técnico"));
        ProgramPackageItems.Add(new ProgramPackageViewModel("Visual Studio Code", "Microsoft.VisualStudioCode", "Editor para scripts/configuración"));
        ProgramPackageItems.Add(new ProgramPackageViewModel("Git", "Git.Git", "Control de versiones"));
        ProgramPackageItems.Add(new ProgramPackageViewModel("PuTTY", "PuTTY.PuTTY", "SSH / Telnet"));
        ProgramPackageItems.Add(new ProgramPackageViewModel("WinSCP", "WinSCP.WinSCP", "Transferencia SFTP / SCP"));
        ProgramPackageItems.Add(new ProgramPackageViewModel("Everything", "voidtools.Everything", "Búsqueda instantánea de archivos"));
        ProgramPackageItems.Add(new ProgramPackageViewModel("CrystalDiskInfo", "CrystalDewWorld.CrystalDiskInfo", "Estado SMART de discos"));
        ProgramPackageItems.Add(new ProgramPackageViewModel("CrystalDiskMark", "CrystalDewWorld.CrystalDiskMark", "Benchmark de discos"));
        ProgramPackageItems.Add(new ProgramPackageViewModel("HWMonitor", "CPUID.HWMonitor", "Monitoreo de hardware"));
        ProgramPackageItems.Add(new ProgramPackageViewModel("Rufus", "Rufus.Rufus", "USB booteable"));
        ProgramPackageItems.Add(new ProgramPackageViewModel("balenaEtcher", "Balena.Etcher", "Creación de USB booteable"));
        ProgramPackageItems.Add(new ProgramPackageViewModel("TeamViewer", "TeamViewer.TeamViewer", "Soporte remoto"));
        ProgramPackageItems.Add(new ProgramPackageViewModel("AnyDesk", "AnyDesk.AnyDesk", "Soporte remoto"));
        ProgramPackageItems.Add(new ProgramPackageViewModel("BleachBit", "BleachBit.BleachBit", "Limpieza de archivos temporales"));

        SelectAllInput = false;
        SelectAllNetwork = false;
        SelectAllCleaner = false;
        SelectAllPrograms = false;
        // ... Check if we want to default false? Yes.
        RefreshCurrentModuleView();
    }

    [RelayCommand]
    private async Task RunOptimizationAsync(CancellationToken cancellationToken)
    {
        if (Data is null)
        {
            StatusMessage = "Sin datos · no se puede ejecutar";
            return;
        }

        IsBusy = true;
        StatusMessage = "Aplicando optimización…";
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
            StatusMessage = "Optimización completada · ver SenaAurion.log";
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
        StatusMessage = $"Aplicando {moduleName}…";
        try
        {
            var selections = ServiceToggles.ToDictionary(
                t => t.ServiceId,
                t => t.IsDisableRequested,
                StringComparer.OrdinalIgnoreCase);

            var options = new OptimizationRunOptions
            {
                ApplyInput = moduleName == "input",
                ApplyNetworkTcp = moduleName == "network",
                ApplyServices = moduleName == "services",
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

        var selectedItems = CurrentModuleItems.Where(i => i.IsSelected && i.IsSelectable).ToArray();
        if (selectedItems.Length == 0)
        {
            StatusMessage = "Selecciona al menos un elemento del módulo.";
            return;
        }

        IsBusy = true;
        if (!IsProgramOperationName(moduleName))
            BeginModuleOperation(moduleName);
        ClearModuleResult(moduleName);
        StatusMessage = $"Aplicando selección en {CurrentModuleTitle}…";
        try
        {
            var before = selectedItems.ToDictionary(i => i, i => i.CurrentStateText);

            if (moduleName == "input")
                await _engine.ApplyTweaksAsync(InputTweakItems.Where(t => t.IsSelected).Select(t => t.Definition), cancellationToken);
            else if (moduleName == "network")
            {
                var regDefs = selectedItems.OfType<RegistryTweakViewModel>().Select(t => t.Definition);
                var quickDefs = selectedItems.OfType<NetworkQuickActionViewModel>().Select(t => t.Definition);
                await _engine.ApplyTweaksAsync(regDefs, cancellationToken).ConfigureAwait(true);
                await _engine.ApplyNetworkQuickActionsAsync(quickDefs, cancellationToken).ConfigureAwait(true);
            }
            else if (moduleName == "services")
                await _engine.ApplyServicesAsync(ServiceTweakItems.Where(t => t.IsSelected).Select(t => t.Definition), cancellationToken);
            else if (moduleName == "cleaner")
                await _engine.ApplyCleanerAsync(CleanerTweakItems.Where(t => t.IsSelected).Select(t => t.Definition), cancellationToken);
            else
            {
                StatusMessage = $"Módulo no soportado: {moduleName}";
                return;
            }

            // Releer estados reales y generar "antes -> después" por ítem seleccionado.
            foreach (var item in selectedItems)
            {
                item.RefreshState();
                var beforeTxt = before.TryGetValue(item, out var b) ? b : "";
                var afterTxt = item.CurrentStateText;

                if (string.Equals(beforeTxt, afterTxt, StringComparison.OrdinalIgnoreCase))
                {
                    item.LastChangeText = "Sin cambios";
                }
                else
                {
                    item.LastChangeText = $"{NormalizeState(beforeTxt)} → {NormalizeState(afterTxt)}";
                }
            }

            // Limpiar textos de cambio en ítems no seleccionados para no confundir.
            foreach (var item in CurrentModuleItems.Except(selectedItems))
            {
                item.LastChangeText = string.Empty;
            }

            StatusMessage = $"Módulo {CurrentModuleTitle} aplicado correctamente.";
            var resultText = moduleName switch
            {
                "services" => "Servicios procesados: deshabilitación aplicada donde correspondía (y bloqueada si era crítica por Wi‑Fi).",
                "cleaner" => "Limpieza finalizada correctamente.",
                "network" => "Ajustes de registro y acciones de red (DNS/caché) ejecutadas según la selección.",
                _ => "Cambios aplicados y verificados."
            };
            SetModuleResult(moduleName, resultText);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operación cancelada.";
            SetModuleResult(moduleName, "Operación cancelada.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            SetModuleResult(moduleName, "Error al aplicar cambios.");
        }
        finally
        {
            EndModuleOperation();
            IsBusy = false;
            OnPropertyChanged(nameof(CurrentModuleIsApplyingVisibility));
            OnPropertyChanged(nameof(CurrentModuleLastResultVisibility));
            OnPropertyChanged(nameof(CurrentModuleSummaryText));
            OnPropertyChanged(nameof(CurrentModuleProgressVisibility));
            OnPropertyChanged(nameof(BackgroundOperationNoticeVisibility));
            OnPropertyChanged(nameof(BackgroundOperationNoticeText));
        }
    }

    private void ResetModuleTransientUiOnSwitch()
    {
        // Evita “información pegada” entre módulos, pero SIN pisar
        // feedback/progreso de una operación activa en segundo plano.
        if (!IsCurrentModuleApplying || string.IsNullOrWhiteSpace(_activeOperationModuleTag))
        {
            if (_moduleResultTextByTag.TryGetValue(SelectedTag, out var moduleResult))
                CurrentModuleLastResultText = moduleResult;
            else
                CurrentModuleLastResultText = string.Empty;
            CurrentModuleLastResultLevel = "none";
        }

        WingetProbeText = string.Empty;
        // No forzar false aquí: si hay operación en curso en otro módulo, debe continuar.

        // Limpiar solo el módulo visible; no borrar historial visual de otros módulos.
        foreach (var item in CurrentModuleItems)
            item.LastChangeText = string.Empty;
    }

    partial void OnIsCurrentModuleApplyingChanged(bool value)
    {
        OnPropertyChanged(nameof(CurrentModuleIsApplyingVisibility));
        OnPropertyChanged(nameof(CurrentModuleProgressVisibility));
        OnPropertyChanged(nameof(BackgroundOperationNoticeVisibility));
        OnPropertyChanged(nameof(BackgroundOperationNoticeText));
    }

    partial void OnIsWingetProbeRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CurrentModuleProgressVisibility));
        OnPropertyChanged(nameof(BackgroundOperationNoticeVisibility));
        OnPropertyChanged(nameof(BackgroundOperationNoticeText));
    }

    partial void OnCurrentModuleLastResultTextChanged(string value) =>
        OnPropertyChanged(nameof(CurrentModuleLastResultVisibility));

    partial void OnWingetProbeTextChanged(string value) =>
        OnPropertyChanged(nameof(WingetProbeTextVisibility));

    partial void OnCurrentModuleLastResultLevelChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentModuleLastResultBrush));
    }

    private void CancelWingetProbeFlows()
    {
        try { _wingetProbeCts?.Cancel(); } catch { }
        try { _wingetProbeHideCts?.Cancel(); } catch { }
    }

    private async Task AutoProbeWingetOnProgramsEntryAsync()
    {
        CancelWingetProbeFlows();
        _wingetProbeCts = new CancellationTokenSource();
        await ProbeWingetInternalAsync(_wingetProbeCts.Token).ConfigureAwait(true);
    }

    private async Task HideWingetIndicatorAfterDelayAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token).ConfigureAwait(true);
            if (!token.IsCancellationRequested && SelectedTag == "programs")
            {
                CurrentModuleLastResultLevel = "none";
                WingetProbeText = string.Empty;
            }
        }
        catch (OperationCanceledException)
        {
            // cancelado intencionalmente
        }
    }

    private async Task ProbeWingetInternalAsync(CancellationToken cancellationToken)
    {
        IsWingetProbeRunning = true;
        CurrentModuleLastResultText = string.Empty;
        CurrentModuleLastResultLevel = "none";
        WingetProbeText = string.Empty;
        StatusMessage = "Comprobando winget…";
        try
        {
            var msg = await WingetProgramService.ProbeAsync(cancellationToken).ConfigureAwait(true);
            var ok = !string.IsNullOrWhiteSpace(msg)
                     && !msg.Contains("no encontrado", StringComparison.OrdinalIgnoreCase)
                     && !msg.StartsWith("Código", StringComparison.OrdinalIgnoreCase);

            WingetProbeText = ok ? "winget instalado" : "winget no instalado";
            StatusMessage = ok ? "winget instalado." : $"winget no instalado: {msg}";

            _wingetProbeHideCts?.Cancel();
            _wingetProbeHideCts = new CancellationTokenSource();
            _ = HideWingetIndicatorAfterDelayAsync(TimeSpan.FromSeconds(15), _wingetProbeHideCts.Token);
        }
        catch (OperationCanceledException)
        {
            // cancelado por cambio de módulo
        }
        finally
        {
            IsWingetProbeRunning = false;
        }
    }

    private string ComputeCurrentModuleSummary()
    {
        if (SelectedTag == "programs")
            return $"Programas disponibles: {ProgramPackageItems.Count}";

        if (SelectedTag == "cleaner")
        {
            var selectedCount = CleanerTweakItems.Count(c => c.IsSelected);
            return selectedCount == 0
                ? "Selecciona tareas de limpieza."
                : $"Tareas seleccionadas: {selectedCount}";
        }

        var items = CurrentModuleItems.Where(IsCountableForSummary).ToArray();
        if (items.Length == 0) return "Sin funciones disponibles en este módulo.";

        var optimized = items.Count(i => i.IsOptimized);
        return $"{optimized}/{items.Length} funciones optimizadas";
    }

    private static bool IsCountableForSummary(TweakItemViewModel item)
    {
        // Para servicios: si no existe, no cuenta. Para el resto siempre cuenta.
        if (item is ServiceTweakModel) return item.IsPresent;
        return true;
    }

    private static string NormalizeState(string raw)
    {
        // Normaliza textos antiguos tipo "[ACTUAL: ...]" o "Estado actual: ..."
        if (string.IsNullOrWhiteSpace(raw)) return "desconocido";
        raw = raw.Trim();
        raw = raw.TrimStart('[', ' ');
        raw = raw.TrimEnd(']', ' ');
        raw = raw.Replace("ACTUAL:", "Estado actual:", StringComparison.OrdinalIgnoreCase);
        return raw;
    }

    [RelayCommand]
    private async Task RevertSelectedModuleAsync(string moduleName, CancellationToken cancellationToken)
    {
        if (Data is null) return;

        if (moduleName != "cleaner")
        {
            var selected = CurrentModuleItems.Where(i => i.IsSelected && i.IsSelectable).ToArray();
            if (selected.Length == 0)
            {
                StatusMessage = "Selecciona al menos un elemento del módulo.";
                return;
            }
        }

        IsBusy = true;
        BeginModuleOperation(moduleName);
        ClearModuleResult(moduleName);
        StatusMessage = $"Revirtiendo selección en {CurrentModuleTitle}…";
        try
        {
            if (moduleName == "input")
                await _engine.RevertInputTweaksAsync(InputTweakItems.Where(t => t.IsSelected).Select(t => t.Definition), cancellationToken);
            else if (moduleName == "network")
                await _engine.RevertNetworkTweaksAsync(
                    NetworkTweakItems.Where(t => t.IsSelected).Select(t => t.Definition), cancellationToken);
            else if (moduleName == "services")
                await _engine.RevertServicesAsync(ServiceTweakItems.Where(t => t.IsSelected).Select(t => t.Definition), cancellationToken);
            else if (moduleName == "cleaner")
                StatusMessage = "El módulo de limpieza no admite reversión.";
            else if (moduleName == "programs")
                await UninstallSelectedProgramsAsync(cancellationToken);

            if (moduleName != "cleaner")
            {
                StatusMessage = $"Módulo {CurrentModuleTitle} revertido correctamente.";
                SetModuleResult(moduleName, "Reversión completada.");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operación cancelada.";
            SetModuleResult(moduleName, "Reversión cancelada.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            SetModuleResult(moduleName, "Error durante reversión.");
        }
        finally
        {
            if (!IsProgramOperationName(moduleName))
                EndModuleOperation();
            IsBusy = false;
            OnPropertyChanged(nameof(CurrentModuleSummaryText));
            OnPropertyChanged(nameof(CurrentModuleProgressVisibility));
            OnPropertyChanged(nameof(BackgroundOperationNoticeVisibility));
            OnPropertyChanged(nameof(BackgroundOperationNoticeText));
        }
    }

    [RelayCommand]
    private async Task RevertAllModuleAsync(string moduleName, CancellationToken cancellationToken)
    {
        if (Data is null) return;
        IsBusy = true;
        BeginModuleOperation(moduleName);
        ClearModuleResult(moduleName);
        StatusMessage = $"Restaurando configuración en {CurrentModuleTitle}…";
        try
        {
            if (moduleName == "cleaner")
            {
                StatusMessage = "La limpieza no tiene restauración de fábrica.";
            }
            else if (moduleName == "programs")
            {
                await UpdateSelectedProgramsAsync(cancellationToken);
                StatusMessage = "Programas seleccionados actualizados a su versión más reciente.";
                SetModuleResult(moduleName, "Actualización de programas completada.");
            }
            else
            {
                await _engine.RevertModuleAsync(moduleName, Data, cancellationToken);
                StatusMessage = $"{CurrentModuleTitle} restaurado a su configuración original.";
                SetModuleResult(moduleName, "Restauración completada.");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operación cancelada.";
            SetModuleResult(moduleName, "Restauración cancelada.");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            SetModuleResult(moduleName, "Error durante restauración.");
        }
        finally
        {
            EndModuleOperation();
            IsBusy = false;
            OnPropertyChanged(nameof(CurrentModuleSummaryText));
            OnPropertyChanged(nameof(CurrentModuleProgressVisibility));
            OnPropertyChanged(nameof(BackgroundOperationNoticeVisibility));
            OnPropertyChanged(nameof(BackgroundOperationNoticeText));
        }
    }

    [RelayCommand]
    private async Task InstallSelectedProgramsAsync(CancellationToken cancellationToken)
    {
        var selected = ProgramPackageItems.Where(p => p.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            StatusMessage = "Selecciona al menos un programa.";
            return;
        }

        IsBusy = true;
        BeginModuleOperation("programs");
        ClearModuleResult("programs");
        CurrentModuleLastResultLevel = "none";
        try
        {
            int ok = 0;
            int fail = 0;
            foreach (var program in selected)
            {
                var result = await _wingetService.InstallAsync(program.PackageId, cancellationToken);
                program.CurrentStateText = result;
                program.LastChangeText = result;
                if (WingetProgramService.IsSuccessResult(result)) ok++; else fail++;
            }

            if (fail == 0)
            {
                StatusMessage = "Instalación finalizada.";
                CurrentModuleLastResultLevel = "success";
                SetModuleResult("programs", "Instalación completada.");
            }
            else
            {
                StatusMessage = "Instalación finalizada con incidencias.";
                CurrentModuleLastResultLevel = "error";
                SetModuleResult("programs", "Instalación con incidencias.");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operación cancelada.";
            SetModuleResult("programs", "Instalación cancelada.");
            CurrentModuleLastResultLevel = "error";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            SetModuleResult("programs", "Error durante la instalación.");
            CurrentModuleLastResultLevel = "error";
        }
        finally
        {
            EndModuleOperation();
            IsBusy = false;
            OnPropertyChanged(nameof(CurrentModuleSummaryText));
            OnPropertyChanged(nameof(CurrentModuleProgressVisibility));
            OnPropertyChanged(nameof(BackgroundOperationNoticeVisibility));
            OnPropertyChanged(nameof(BackgroundOperationNoticeText));
        }
    }

    [RelayCommand]
    private async Task UpdateSelectedProgramsAsync(CancellationToken cancellationToken)
    {
        var selected = ProgramPackageItems.Where(p => p.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            StatusMessage = "Selecciona al menos un programa.";
            return;
        }

        IsBusy = true;
        BeginModuleOperation("programs");
        ClearModuleResult("programs");
        CurrentModuleLastResultLevel = "none";
        try
        {
            int ok = 0;
            int fail = 0;
            foreach (var program in selected)
            {
                var result = await _wingetService.UpgradeAsync(program.PackageId, cancellationToken);
                program.CurrentStateText = result;
                program.LastChangeText = result;
                if (WingetProgramService.IsSuccessResult(result)) ok++; else fail++;
            }

            if (fail == 0)
            {
                StatusMessage = "Actualización finalizada.";
                CurrentModuleLastResultLevel = "success";
                SetModuleResult("programs", "Actualización completada.");
            }
            else
            {
                StatusMessage = "Actualización finalizada con incidencias.";
                CurrentModuleLastResultLevel = "error";
                SetModuleResult("programs", "Actualización con incidencias.");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operación cancelada.";
            SetModuleResult("programs", "Actualización cancelada.");
            CurrentModuleLastResultLevel = "error";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            SetModuleResult("programs", "Error durante la actualización.");
            CurrentModuleLastResultLevel = "error";
        }
        finally
        {
            EndModuleOperation();
            IsBusy = false;
            OnPropertyChanged(nameof(CurrentModuleSummaryText));
            OnPropertyChanged(nameof(CurrentModuleProgressVisibility));
            OnPropertyChanged(nameof(BackgroundOperationNoticeVisibility));
            OnPropertyChanged(nameof(BackgroundOperationNoticeText));
        }
    }

    [RelayCommand]
    private async Task UninstallSelectedProgramsAsync(CancellationToken cancellationToken)
    {
        var selected = ProgramPackageItems.Where(p => p.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            StatusMessage = "Selecciona al menos un programa.";
            return;
        }

        IsBusy = true;
        BeginModuleOperation("programs");
        ClearModuleResult("programs");
        CurrentModuleLastResultLevel = "none";
        try
        {
            int ok = 0;
            int fail = 0;
            foreach (var program in selected)
            {
                var result = await _wingetService.UninstallAsync(program.PackageId, cancellationToken);
                program.CurrentStateText = result;
                program.LastChangeText = result;
                if (WingetProgramService.IsSuccessResult(result)) ok++; else fail++;
            }

            if (fail == 0)
            {
                StatusMessage = "Desinstalación finalizada.";
                CurrentModuleLastResultLevel = "success";
                SetModuleResult("programs", "Desinstalación completada.");
            }
            else
            {
                StatusMessage = "Desinstalación finalizada con incidencias.";
                CurrentModuleLastResultLevel = "error";
                SetModuleResult("programs", "Desinstalación con incidencias.");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operación cancelada.";
            SetModuleResult("programs", "Desinstalación cancelada.");
            CurrentModuleLastResultLevel = "error";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            SetModuleResult("programs", "Error durante la desinstalación.");
            CurrentModuleLastResultLevel = "error";
        }
        finally
        {
            EndModuleOperation();
            IsBusy = false;
            OnPropertyChanged(nameof(CurrentModuleSummaryText));
            OnPropertyChanged(nameof(CurrentModuleProgressVisibility));
            OnPropertyChanged(nameof(BackgroundOperationNoticeVisibility));
            OnPropertyChanged(nameof(BackgroundOperationNoticeText));
        }
    }

    public readonly record struct NavItem(string Tag, string Label, string Glyph);

    [RelayCommand]
    private void ToggleSelectAllCurrentModule()
    {
        switch (SelectedTag)
        {
            case "input":
                SelectAllInput = !AreAllSafeInputSelected();
                break;
            case "network":
                SelectAllNetwork = !AreAllNetworkSafeSelected();
                break;
            case "services":
                SelectAllServices = !AreAllSelectableServicesSelected();
                break;
            case "cleaner":
                SelectAllCleaner = !AreAllSafeCleanerSelected();
                break;
            case "programs":
                SelectAllPrograms = !AreAllSelected(ProgramPackageItems);
                break;
        }
    }

    private static bool AreAllSelected<T>(IEnumerable<T> items) where T : TweakItemViewModel =>
        items.Any() && items.All(i => i.IsSelected);

    private bool AreAllSelectableServicesSelected()
    {
        var selectable = ServiceTweakItems.Where(s => !s.IsWifiLocked).ToArray();
        return selectable.Length > 0 && selectable.All(s => s.IsSelected);
    }

    private bool AreAllSafeCleanerSelected()
    {
        var safe = CleanerTweakItems.Where(c => !c.IsDanger).ToArray();
        return safe.Length > 0 && safe.All(c => c.IsSelected);
    }

    private bool AreAllNetworkSafeSelected()
    {
        var items = NetworkTweakItems.Where(t => !t.IsDanger)
            .Cast<TweakItemViewModel>()
            .Concat(NetworkQuickActionItems.Where(t => !t.IsDanger))
            .ToArray();
        return items.Length > 0 && items.All(i => i.IsSelected);
    }

    private bool AreAllSafeInputSelected()
    {
        var safe = InputTweakItems.Where(t => !t.IsDanger).ToArray();
        return safe.Length > 0 && safe.All(i => i.IsSelected);
    }

    /// <summary>Aplica un perfil de entrada (solo marca casillas; usa «Aplicar» para ejecutar).</summary>
    [RelayCommand]
    private void ApplyInputProfile(string? profileId)
    {
        if (Data is null || string.IsNullOrWhiteSpace(profileId)) return;
        var profile = Data.InputProfiles.FirstOrDefault(p =>
            string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            StatusMessage = $"Perfil de entrada no encontrado: {profileId}";
            return;
        }

        var enable = new HashSet<string>(profile.EnableTweakIds, StringComparer.OrdinalIgnoreCase);

        foreach (var item in InputTweakItems)
        {
            item.IsSelected = enable.Contains(item.Definition.Id);
        }

        StatusMessage = $"Perfil «{profile.Label}»: selección actualizada. Pulsa Aplicar para ejecutar los ajustes de entrada.";
        OnPropertyChanged(nameof(CurrentModuleSummaryText));
    }

    /// <summary>Aplica un perfil de red (solo marca casillas; usa «Aplicar» para ejecutar).</summary>
    [RelayCommand]
    private void ApplyNetworkProfile(string? profileId)
    {
        if (Data is null || string.IsNullOrWhiteSpace(profileId)) return;
        var profile = Data.NetworkProfiles.FirstOrDefault(p =>
            string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            StatusMessage = $"Perfil de red no encontrado: {profileId}";
            return;
        }

        var enable = new HashSet<string>(profile.EnableTweakIds, StringComparer.OrdinalIgnoreCase);

        foreach (var item in NetworkTweakItems)
        {
            item.IsSelected = enable.Contains(item.Definition.Id);
        }

        StatusMessage = $"Perfil «{profile.Label}»: selección actualizada. Pulsa Aplicar para ejecutar los ajustes de red.";
        OnPropertyChanged(nameof(CurrentModuleSummaryText));
    }

    /// <summary>Ejecuta una acción rápida de red directamente (flush DNS, release/renew, etc.).</summary>
    [RelayCommand]
    private async Task ExecuteNetworkQuickAction(string? actionId, CancellationToken cancellationToken)
    {
        if (Data is null || string.IsNullOrWhiteSpace(actionId)) return;
        var action = Data.NetworkQuickActions.FirstOrDefault(a =>
            string.Equals(a.Id, actionId, StringComparison.OrdinalIgnoreCase));
        if (action is null)
        {
            StatusMessage = $"Acción de red no encontrada: {actionId}";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Ejecutando {action.DisplayLabel}…";
        try
        {
            var engine = _engine as Services.OptimizationEngine;
            if (engine is not null)
            {
                await engine.ApplyNetworkQuickActionAsync(action, cancellationToken).ConfigureAwait(true);
                StatusMessage = $"{action.DisplayLabel}: completado.";
            }
            else
            {
                StatusMessage = "Motor de optimización no disponible.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error en {action.DisplayLabel}: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            SystemStateMonitor.NotifyStateChanged();
        }
    }

    /// <summary>Aplica un perfil de servicios (solo marca casillas; usa «Aplicar» para ejecutar).</summary>
    [RelayCommand]
    private void ApplyServiceProfile(string? profileId)
    {
        if (Data is null || string.IsNullOrWhiteSpace(profileId)) return;
        var profile = Data.ServiceProfiles.FirstOrDefault(p =>
            string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            StatusMessage = $"Perfil no encontrado: {profileId}";
            return;
        }

        var disable = new HashSet<string>(profile.DisableServiceIds, StringComparer.OrdinalIgnoreCase);

        foreach (var item in ServiceTweakItems)
        {
            var want = disable.Contains(item.Definition.Id);
            if (item.IsWifiLocked)
                item.IsSelected = false;
            else
                item.IsSelected = want;
        }

        foreach (var toggle in ServiceToggles)
        {
            var want = disable.Contains(toggle.ServiceId);
            if (toggle.IsWifiLocked)
                toggle.IsDisableRequested = false;
            else
                toggle.IsDisableRequested = want;
        }

        StatusMessage = $"Perfil «{profile.Label}»: selección actualizada. Pulsa Aplicar para deshabilitar servicios.";
        OnPropertyChanged(nameof(CurrentModuleSummaryText));
    }

    [RelayCommand]
    private async Task ProbeWingetAsync(CancellationToken cancellationToken)
    {
        await ProbeWingetInternalAsync(cancellationToken).ConfigureAwait(true);
    }
}

