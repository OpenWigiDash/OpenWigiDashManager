using System.Runtime.InteropServices;
using static OpenWigi.Device.Usb.NativeMethods;

namespace OpenWigi.Device.Usb;

/// <summary>
/// Reset do WigiDash pelo Windows, o equivalente a tirar e pôr o cabo. Serve para quando o aparelho
/// responde ao controle mas para de aceitar escrita bulk (timeout em toda escrita), que só voltava
/// desligando o cabo. Precisa de administrador (o serviço roda como LocalSystem) e de nenhum handle
/// aberto no aparelho.
/// </summary>
public static class UsbDeviceReset
{
    /// <summary>
    /// Tenta, nesta ordem: desligar e religar a porta do hub (o aparelho reinicia de verdade) e, se o hub
    /// não aceitar, desativar e reativar o dispositivo no Windows (recarrega o driver). Nunca lança;
    /// <paramref name="detail"/> conta o que foi feito ou por que não deu.
    /// </summary>
    public static bool TryReset(string devicePath, out string detail)
    {
        try
        {
            var device = UsbDeviceNode(devicePath);
            if (device is null)
            {
                detail = "o aparelho não está mais na árvore de dispositivos";
                return false;
            }

            if (TryCyclePort(device.Value, out var cycle))
            {
                detail = cycle;
                return true;
            }

            if (TryRestartNode(device.Value, out var restart))
            {
                detail = $"{restart} (o hub recusou o reset da porta: {cycle})";
                return true;
            }

            detail = $"{cycle}; {restart}";
            return false;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or IOException)
        {
            detail = ex.Message;
            return false;
        }
    }

    // Nó do dispositivo USB inteiro. Se o WinUSB estiver numa interface de um aparelho composto
    // (USB\VID_...&MI_00), o dispositivo é o pai dela.
    private static uint? UsbDeviceNode(string devicePath)
    {
        var instanceId = InterfaceInstanceId(devicePath);
        if (instanceId is null || CM_Locate_DevNodeW(out var node, instanceId, 0) != CR_SUCCESS)
            return null;

        if (instanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase)
            && CM_Get_Parent(out var parent, node, 0) == CR_SUCCESS)
            return parent;
        return node;
    }

    private static bool TryCyclePort(uint device, out string detail)
    {
        if (!TryGetUInt32(device, DEVPKEY_Device_Address, out var port) || port == 0)
        {
            detail = "porta do hub desconhecida";
            return false;
        }
        if (CM_Get_Parent(out var hub, device, 0) != CR_SUCCESS || HubInterfacePath(hub) is not { } hubPath)
        {
            detail = "hub do aparelho não encontrado";
            return false;
        }

        using var handle = CreateFileW(hubPath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, 0,
            OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, 0);
        if (handle.IsInvalid)
        {
            detail = $"hub não abriu (erro {Marshal.GetLastPInvokeError()})";
            return false;
        }

        var parameters = new USB_CYCLE_PORT_PARAMS { ConnectionIndex = port };
        unsafe
        {
            var size = (uint)sizeof(USB_CYCLE_PORT_PARAMS);
            if (!DeviceIoControl(handle, IOCTL_USB_HUB_CYCLE_PORT, &parameters, size, &parameters, size, out _, 0))
            {
                detail = $"reset da porta {port} recusado (erro {Marshal.GetLastPInvokeError()})";
                return false;
            }
        }

        detail = $"porta {port} do hub desligada e religada";
        return true;
    }

    private static bool TryRestartNode(uint device, out string detail)
    {
        var disabled = CM_Disable_DevNode(device, CM_DISABLE_UI_NOT_OK);
        if (disabled != CR_SUCCESS)
        {
            detail = $"desativar o dispositivo falhou (CR {disabled})";
            return false;
        }

        // Sem CM_DISABLE_PERSIST: mesmo se o enable falhar, o aparelho volta sozinho no próximo boot ou cabo.
        var enabled = CM_Enable_DevNode(device, 0);
        detail = enabled == CR_SUCCESS
            ? "dispositivo desativado e reativado no Windows"
            : $"dispositivo desativado, mas reativar falhou (CR {enabled})";
        return enabled == CR_SUCCESS;
    }

    private static unsafe string? InterfaceInstanceId(string devicePath)
    {
        uint size = 0;
        CM_Get_Device_Interface_PropertyW(devicePath, DEVPKEY_Device_InstanceId, out _, null, ref size, 0);
        if (size == 0)
            return null;

        var buffer = new byte[size];
        fixed (byte* p = buffer)
        {
            if (CM_Get_Device_Interface_PropertyW(devicePath, DEVPKEY_Device_InstanceId, out var type, p, ref size, 0) != CR_SUCCESS
                || type != DEVPROP_TYPE_STRING)
                return null;
            return new string((char*)p).TrimEnd('\0');
        }
    }

    private static unsafe bool TryGetUInt32(uint node, in DEVPROPKEY key, out uint value)
    {
        uint result = 0;
        var size = (uint)sizeof(uint);
        var ok = CM_Get_DevNode_PropertyW(node, key, out var type, (byte*)&result, ref size, 0) == CR_SUCCESS
            && type == DEVPROP_TYPE_UINT32;
        value = result;
        return ok;
    }

    private static unsafe string? HubInterfacePath(uint hub)
    {
        var hubId = DeviceInstanceId(hub);
        if (hubId is null)
            return null;

        var guid = GUID_DEVINTERFACE_USB_HUB;
        if (CM_Get_Device_Interface_List_SizeW(out var length, guid, hubId, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS
            || length <= 1)
            return null;

        var buffer = new char[length];
        fixed (char* p = buffer)
        {
            if (CM_Get_Device_Interface_ListW(guid, hubId, p, length, CM_GET_DEVICE_INTERFACE_LIST_PRESENT) != CR_SUCCESS)
                return null;
            // Lista de strings terminadas em \0; o hub tem uma interface só.
            var first = new string(p);
            return first.Length > 0 ? first : null;
        }
    }

    private static unsafe string? DeviceInstanceId(uint node)
    {
        uint size = 0;
        CM_Get_DevNode_PropertyW(node, DEVPKEY_Device_InstanceId, out _, null, ref size, 0);
        if (size == 0)
            return null;

        var buffer = new byte[size];
        fixed (byte* p = buffer)
        {
            if (CM_Get_DevNode_PropertyW(node, DEVPKEY_Device_InstanceId, out var type, p, ref size, 0) != CR_SUCCESS
                || type != DEVPROP_TYPE_STRING)
                return null;
            return new string((char*)p).TrimEnd('\0');
        }
    }
}
