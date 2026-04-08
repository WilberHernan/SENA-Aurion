namespace SenaAurion.Services;

public static class RegistryValueFormatter
{
    public static string ToDisplayString(object? value)
    {
        if (value is null) return "";
        if (value is string s) return s;
        if (value is string[] arr) return string.Join(", ", arr);
        return value.ToString() ?? "";
    }
}

