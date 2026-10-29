using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using OpenWigi.Device.Protocol;

namespace OpenWigi.Manager.Services;

/// <summary>Converte a prévia RGB565 do aparelho em PNG para a UI mostrar.</summary>
public static class PreviewImage
{
    public static string ToDataUrl(byte[] rgb565)
    {
        using var bitmap = new Bitmap(WigiDashProtocol.Width, WigiDashProtocol.Height, PixelFormat.Format16bppRgb565);
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
        try
        {
            var row = WigiDashProtocol.Width * 2;
            for (var y = 0; y < bitmap.Height; y++)
                Marshal.Copy(rgb565, y * row, data.Scan0 + y * data.Stride, row);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        using var png = new MemoryStream();
        bitmap.Save(png, ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(png.ToArray());
    }
}
