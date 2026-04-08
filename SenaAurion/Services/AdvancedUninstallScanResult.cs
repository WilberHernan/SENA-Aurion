namespace SenaAurion.Services;

public enum ResidueCategory
{
    Folders,
    Files,
    Registry,
    Services,
    ScheduledTasks
}

public enum ResidueItemType
{
    Folder,
    File,
    RegistryKey,
    RegistryValue,
    Service,
    ScheduledTask
}

public sealed class ResidueItem
{
    public ResidueCategory Category { get; init; }
    public ResidueItemType Type { get; init; }
    public string Path { get; init; } = "";
}

public sealed class AdvancedUninstallScanResult
{
    public string ProgramName { get; init; } = "";
    public IReadOnlyList<ResidueItem> Items { get; init; } = Array.Empty<ResidueItem>();
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
}

