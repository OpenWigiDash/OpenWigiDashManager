using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OpenWigi.Device.Usb;

internal static class NativeMethods
{
    public const int DIGCF_PRESENT = 0x02;
    public const int DIGCF_DEVICEINTERFACE = 0x10;
    public const int ERROR_NO_MORE_ITEMS = 259;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;

    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x1;
    public const uint FILE_SHARE_WRITE = 0x2;
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    public const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    public const uint PIPE_TRANSFER_TIMEOUT = 0x03;

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public nint Reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct WINUSB_SETUP_PACKET
    {
        public byte RequestType;
        public byte Request;
        public ushort Value;
        public ushort Index;
        public ushort Length;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern nint SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator, nint hwndParent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiEnumDeviceInterfaces(nint deviceInfoSet, nint deviceInfoData, ref Guid interfaceClassGuid,
        int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiGetDeviceInterfaceDetailW(nint deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        nint deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, out int requiredSize, nint deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinUsb_Initialize(SafeFileHandle deviceHandle, out nint interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinUsb_Free(nint interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern unsafe bool WinUsb_ControlTransfer(nint interfaceHandle, WINUSB_SETUP_PACKET setupPacket, byte* buffer,
        uint bufferLength, out uint lengthTransferred, nint overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern unsafe bool WinUsb_WritePipe(nint interfaceHandle, byte pipeId, byte* buffer, uint bufferLength,
        out uint lengthTransferred, nint overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinUsb_AbortPipe(nint interfaceHandle, byte pipeId);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinUsb_ResetPipe(nint interfaceHandle, byte pipeId);

    [DllImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern unsafe bool WinUsb_SetPipePolicy(nint interfaceHandle, byte pipeId, uint policyType, uint valueLength, void* value);

    // Reset do aparelho (UsbDeviceReset): árvore de dispositivos do cfgmgr32 e IOCTL do hub.

    public const int CR_SUCCESS = 0;
    public const uint CM_GET_DEVICE_INTERFACE_LIST_PRESENT = 0;
    public const uint CM_DISABLE_UI_NOT_OK = 0x4;
    public const uint IOCTL_USB_HUB_CYCLE_PORT = 0x220444; // CTL_CODE(FILE_DEVICE_USB, 273, METHOD_BUFFERED, FILE_ANY_ACCESS)
    public const uint DEVPROP_TYPE_STRING = 0x12;
    public const uint DEVPROP_TYPE_UINT32 = 0x7;

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct USB_CYCLE_PORT_PARAMS
    {
        public uint ConnectionIndex;
        public uint StatusReturned;
    }

    public static readonly DEVPROPKEY DEVPKEY_Device_InstanceId = new() { fmtid = new("78c34fc8-104a-4aca-9ea4-524d52996e57"), pid = 256 };
    public static readonly DEVPROPKEY DEVPKEY_Device_Address = new() { fmtid = new("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 30 };
    public static readonly Guid GUID_DEVINTERFACE_USB_HUB = new("f18a0e88-c30c-11d0-8815-00a0c906bed8");

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern unsafe int CM_Get_Device_Interface_PropertyW(string deviceInterface, in DEVPROPKEY propertyKey,
        out uint propertyType, byte* buffer, ref uint bufferSize, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern unsafe int CM_Get_DevNode_PropertyW(uint devInst, in DEVPROPKEY propertyKey, out uint propertyType,
        byte* buffer, ref uint bufferSize, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern int CM_Get_Device_Interface_List_SizeW(out uint length, in Guid interfaceClassGuid, string? deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    public static extern unsafe int CM_Get_Device_Interface_ListW(in Guid interfaceClassGuid, string? deviceId, char* buffer,
        uint length, uint flags);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Disable_DevNode(uint devInst, uint flags);

    [DllImport("cfgmgr32.dll")]
    public static extern int CM_Enable_DevNode(uint devInst, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern unsafe bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, void* inBuffer, uint inBufferSize,
        void* outBuffer, uint outBufferSize, out uint bytesReturned, nint overlapped);
}
