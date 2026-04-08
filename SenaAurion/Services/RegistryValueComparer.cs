using Microsoft.Win32;

namespace SenaAurion.Services;

public static class RegistryValueComparer
{
    public static bool AreEquivalent(object? current, object? expected, RegistryValueKind kind)
    {
        if (current is null && expected is null) return true;
        if (current is null || expected is null) return false;

        if (kind == RegistryValueKind.MultiString)
        {
            if (current is string[] cArr && expected is string[] eArr)
                return cArr.SequenceEqual(eArr, StringComparer.OrdinalIgnoreCase);

            if (current is string cStr && expected is string[] eArr2)
                return new[] { cStr }.SequenceEqual(eArr2, StringComparer.OrdinalIgnoreCase);

            if (current is string[] cArr2 && expected is string eStr)
                return cArr2.SequenceEqual(new[] { eStr }, StringComparer.OrdinalIgnoreCase);
        }

        // DWord/QWord sometimes come back as int/long
        if (kind == RegistryValueKind.DWord)
        {
            var c = ToLong(current);
            var e = ToLong(expected);
            return c.HasValue && e.HasValue && c.Value == e.Value;
        }

        if (kind == RegistryValueKind.QWord)
        {
            var c = ToLong(current);
            var e = ToLong(expected);
            return c.HasValue && e.HasValue && c.Value == e.Value;
        }

        var cTxt = RegistryValueFormatter.ToDisplayString(current).Trim();
        var eTxt = RegistryValueFormatter.ToDisplayString(expected).Trim();
        return string.Equals(cTxt, eTxt, StringComparison.OrdinalIgnoreCase);
    }

    private static long? ToLong(object o)
    {
        return o switch
        {
            int i => i,
            long l => l,
            uint ui => ui,
            ulong ul => ul <= long.MaxValue ? (long)ul : null,
            string s when long.TryParse(s, out var v) => v,
            _ => null
        };
    }
}

