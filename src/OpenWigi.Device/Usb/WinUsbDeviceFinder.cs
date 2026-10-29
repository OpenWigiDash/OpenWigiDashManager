using System.Runtime.InteropServices;
using static OpenWigi.Device.Usb.NativeMethods;

namespace OpenWigi.Device.Usb;

/// <summary>Lista os WigiDash conectados pela interface WinUSB que o driver registra.</summary>
public static class WinUsbDeviceFinder
{
    /// <summary>GUID da interface de dispositivo publicada pelo driver do WigiDash.</summary>
    public static readonly Guid InterfaceGuid = new("D876A186-7B31-4804-8115-79A87E8941BD");

    public static IReadOnlyList<string> FindDevicePaths()
    {
        var guid = InterfaceGuid;
        var set = SetupDiGetClassDevsW(ref guid, null, 0, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == -1)
            return [];

        var paths = new List<string>();
        try
        {
            for (var index = 0; ; index++)
            {
                var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, 0, ref guid, index, ref data))
                    break;

                SetupDiGetDeviceInterfaceDetailW(set, ref data, 0, 0, out var size, 0);
                if (size == 0)
                    continue;

                var detail = Marshal.AllocHGlobal(size);
                try
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA_W: cbSize é 8 em 64 bits e 6 em 32 bits.
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, size, out _, 0))
                    {
                        var path = Marshal.PtrToStringUni(detail + 4);
                        if (!string.IsNullOrEmpty(path))
                            paths.Add(path);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return paths;
    }
}
