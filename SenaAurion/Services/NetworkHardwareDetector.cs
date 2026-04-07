using System.Net.NetworkInformation;

namespace SenaAurion.Services;

/// <summary>DetecciÃ³n de hardware real: adaptador Wiâ€‘Fi activo (802.11).</summary>
public static class NetworkHardwareDetector
{
    /// <summary>True si existe al menos una interfaz inalÃ¡mbrica con estado Up.</summary>
    public static bool IsWirelessAdapterActive()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    return true;
            }
        }
        catch
        {
            /* no fallar */
        }

        return false;
    }
}

