using System.ComponentModel;
using Microsoft.Win32.SafeHandles;
using static OpenWigi.Device.Usb.NativeMethods;

namespace OpenWigi.Device.Usb;

/// <summary>Transporte WinUSB direto (winusb.dll), sem bibliotecas de terceiros.</summary>
public sealed class WinUsbTransport : IUsbTransport
{
    private const byte RequestTypeIn = 0xA1;
    private const byte RequestTypeOut = 0x21;
    private const byte DataOutPipe = 0x01;
    private const byte ControlPipe = 0x00;

    private readonly SafeFileHandle _file;
    private nint _winUsb;

    private WinUsbTransport(SafeFileHandle file, nint winUsb)
    {
        _file = file;
        _winUsb = winUsb;
    }

    public string DevicePath { get; private init; } = string.Empty;

    /// <summary>Abre o dispositivo pelo caminho da interface (veja <see cref="WinUsbDeviceFinder"/>).</summary>
    public static WinUsbTransport Open(string devicePath, TimeSpan? timeout = null)
    {
        var file = CreateFileW(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, 0,
            OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_OVERLAPPED, 0);
        if (file.IsInvalid)
        {
            var error = new Win32Exception();
            file.Dispose();
            throw new IOException($"Não foi possível abrir o dispositivo (está em uso por outro programa?): {error.Message}", error);
        }

        if (!WinUsb_Initialize(file, out var handle))
        {
            var error = new Win32Exception();
            file.Dispose();
            throw new IOException($"WinUsb_Initialize falhou: {error.Message}", error);
        }

        var transport = new WinUsbTransport(file, handle) { DevicePath = devicePath };
        var ms = (uint)(timeout ?? TimeSpan.FromSeconds(1)).TotalMilliseconds;
        transport.SetTimeout(ControlPipe, ms);
        transport.SetTimeout(DataOutPipe, ms);
        // Uma sessão anterior que caiu no meio de uma escrita pode ter deixado o endpoint parado (STALL) ou
        // com o data toggle trocado. O reset do pipe limpa os dois; se falhar, a primeira escrita mostra.
        WinUsb_ResetPipe(handle, DataOutPipe);
        return transport;
    }

    public unsafe int ControlIn(byte request, ushort value, Span<byte> buffer)
    {
        var setup = new WINUSB_SETUP_PACKET
        {
            RequestType = RequestTypeIn,
            Request = request,
            Value = value,
            Index = 0,
            Length = (ushort)buffer.Length,
        };
        fixed (byte* p = buffer)
        {
            if (!WinUsb_ControlTransfer(Handle, setup, p, (uint)buffer.Length, out var read, 0))
                throw Fail($"control IN 0x{request:X2}");
            return (int)read;
        }
    }

    public unsafe void ControlOut(byte request, ushort value, ReadOnlySpan<byte> data)
    {
        var setup = new WINUSB_SETUP_PACKET
        {
            RequestType = RequestTypeOut,
            Request = request,
            Value = value,
            Index = 0,
            Length = (ushort)data.Length,
        };
        fixed (byte* p = data)
        {
            if (!WinUsb_ControlTransfer(Handle, setup, p, (uint)data.Length, out _, 0))
                throw Fail($"control OUT 0x{request:X2}");
        }
    }

    public unsafe int BulkWrite(ReadOnlySpan<byte> data)
    {
        fixed (byte* p = data)
        {
            if (!WinUsb_WritePipe(Handle, DataOutPipe, p, (uint)data.Length, out var written, 0))
            {
                var error = Fail("bulk OUT");
                RecoverPipe(DataOutPipe);
                throw error;
            }
            return (int)written;
        }
    }

    public void Dispose()
    {
        if (_winUsb != 0)
        {
            WinUsb_Free(_winUsb);
            _winUsb = 0;
        }
        _file.Dispose();
    }

    private nint Handle => _winUsb != 0 ? _winUsb : throw new ObjectDisposedException(nameof(WinUsbTransport));

    // Depois de uma escrita que deu timeout, o pedido pode ter ficado pendurado no pipe e o endpoint em
    // STALL. Cancela o que sobrou e manda CLEAR_FEATURE(ENDPOINT_HALT), para a próxima escrita não
    // herdar o problema. Sem retorno: se não der, quem chamou já vai receber o erro original.
    private void RecoverPipe(byte pipe)
    {
        WinUsb_AbortPipe(_winUsb, pipe);
        WinUsb_ResetPipe(_winUsb, pipe);
    }

    private unsafe void SetTimeout(byte pipe, uint milliseconds)
    {
        if (!WinUsb_SetPipePolicy(Handle, pipe, PIPE_TRANSFER_TIMEOUT, sizeof(uint), &milliseconds))
            throw Fail($"timeout do pipe 0x{pipe:X2}");
    }

    private static IOException Fail(string what)
    {
        var error = new Win32Exception();
        return new IOException($"Falha USB em {what}: {error.Message}", error);
    }
}
