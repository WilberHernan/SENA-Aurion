using System.Text.Json;
using System.Text.Json.Serialization;

namespace SenaAurion.Models;

/// <summary>RaÃ­z deserializada de OptimizationData.json (fuente Ãºnica de verdad offline).</summary>
public sealed class OptimizationDataDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("metadata")]
    public OptimizationMetadata Metadata { get; init; } = new();

    [JsonPropertyName("inputLatency")]
    public InputLatencySection InputLatency { get; init; } = new();

    [JsonPropertyName("networkTcp")]
    public NetworkTcpSection NetworkTcp { get; init; } = new();

    /// <summary>Acciones de red fuera del registro (DNS, flush, etc.).</summary>
    [JsonPropertyName("networkQuickActions")]
    public IList<NetworkQuickActionDefinition> NetworkQuickActions { get; init; } = new List<NetworkQuickActionDefinition>();

    [JsonPropertyName("registryTweaks")]
    public IList<RegistryTweakDefinition> RegistryTweaks { get; init; } = new List<RegistryTweakDefinition>();

    [JsonPropertyName("services")]
    public IList<ServiceDefinition> Services { get; init; } = new List<ServiceDefinition>();

    /// <summary>Perfiles predefinidos de entrada: lista de ids de tweaks a marcar.</summary>
    [JsonPropertyName("inputProfiles")]
    public IList<InputProfileDefinition> InputProfiles { get; init; } = new List<InputProfileDefinition>();

    /// <summary>Perfiles predefinidos de red: lista de ids de tweaks a marcar.</summary>
    [JsonPropertyName("networkProfiles")]
    public IList<NetworkProfileDefinition> NetworkProfiles { get; init; } = new List<NetworkProfileDefinition>();

    /// <summary>Perfiles predefinidos: lista de ids de servicio a marcar para deshabilitar.</summary>
    [JsonPropertyName("serviceProfiles")]
    public IList<ServiceProfileDefinition> ServiceProfiles { get; init; } = new List<ServiceProfileDefinition>();

    [JsonPropertyName("cleaner")]
    public CleanerSection Cleaner { get; init; } = new();
}

public sealed class NetworkQuickActionDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("displayLabel")]
    public string DisplayLabel { get; init; } = "";

    [JsonPropertyName("group")]
    public string Group { get; init; } = "Diagnostico";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    /// <summary>flushDns | dnsDhcpReset | releaseRenew | resetWinsock | resetTcpIp</summary>
    [JsonPropertyName("action")]
    public string Action { get; init; } = "";

    [JsonPropertyName("isDanger")]
    public bool IsDanger { get; init; }

    [JsonPropertyName("impactDescription")]
    public string ImpactDescription { get; init; } = "";
}

public sealed class ServiceProfileDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("label")]
    public string Label { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("disableServiceIds")]
    public IList<string> DisableServiceIds { get; init; } = new List<string>();
}

public sealed class InputProfileDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("label")]
    public string Label { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("enableTweakIds")]
    public IList<string> EnableTweakIds { get; init; } = new List<string>();
}

public sealed class NetworkProfileDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("label")]
    public string Label { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("enableTweakIds")]
    public IList<string> EnableTweakIds { get; init; } = new List<string>();
}

public sealed class CleanerSection
{
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("tasks")]
    public IList<CleanerTaskDefinition> Tasks { get; init; } = new List<CleanerTaskDefinition>();
}

public sealed class CleanerTaskDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("displayLabel")]
    public string DisplayLabel { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("defaultRecommended")]
    public bool DefaultRecommended { get; init; }

    [JsonPropertyName("isDanger")]
    public bool IsDanger { get; init; }
}

public sealed class OptimizationMetadata
{
    [JsonPropertyName("product")]
    public string Product { get; init; } = "";

    [JsonPropertyName("targetOs")]
    public string TargetOs { get; init; } = "";

    [JsonPropertyName("alignmentNotes")]
    public IList<string> AlignmentNotes { get; init; } = new List<string>();
}

public sealed class InputLatencySection
{
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("tweaks")]
    public IList<RegistryTweakDefinition> Tweaks { get; init; } = new List<RegistryTweakDefinition>();
}

public sealed class NetworkTcpSection
{
    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("wifiCriticalServices")]
    public IList<string> WifiCriticalServices { get; init; } = new List<string>();

    [JsonPropertyName("tweaks")]
    public IList<RegistryTweakDefinition> Tweaks { get; init; } = new List<RegistryTweakDefinition>();
}

public sealed class RegistryTweakDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = "";

    [JsonPropertyName("category")]
    public string Category { get; init; } = "";

    [JsonPropertyName("hive")]
    public string Hive { get; init; } = "CurrentUser";

    [JsonPropertyName("subKey")]
    public string SubKey { get; init; } = "";

    [JsonPropertyName("valueName")]
    public string ValueName { get; init; } = "";

    [JsonPropertyName("valueKind")]
    public string ValueKind { get; init; } = "DWord";

    [JsonPropertyName("valueData")]
    public JsonElement? ValueData { get; init; }

    [JsonPropertyName("requiresAdmin")]
    public bool RequiresAdmin { get; init; }

    [JsonPropertyName("rationale")]
    public string Rationale { get; init; } = "";

    [JsonPropertyName("sourceAlignment")]
    public string SourceAlignment { get; init; } = "";

    [JsonPropertyName("isDanger")]
    public bool IsDanger { get; init; }

    [JsonPropertyName("impactDescription")]
    public string ImpactDescription { get; init; } = "";
}

public sealed class ServiceDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("serviceName")]
    public string ServiceName { get; init; } = "";

    [JsonPropertyName("displayLabel")]
    public string DisplayLabel { get; init; } = "";

    [JsonPropertyName("group")]
    public string ServiceGroup { get; init; } = "";

    [JsonPropertyName("defaultRecommended")]
    public bool DefaultRecommended { get; init; }

    [JsonPropertyName("neverDisableWhenWifi")]
    public bool NeverDisableWhenWifi { get; init; }

    [JsonPropertyName("requiresAdmin")]
    public bool RequiresAdmin { get; init; } = true;

    [JsonPropertyName("userDescription")]
    public string UserDescription { get; init; } = "";
}

