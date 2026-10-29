using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using OpenWigi.Device.Display;
using OpenWigi.Device.Protocol;

namespace OpenWigi.Service.Device;

/// <summary>Tela que o serviço mostra quando nenhuma sessão tem a posse: relógio e o motivo.</summary>
public static class ServiceScreen
{
    public static byte[] Render(string reason, DateTime now)
    {
        const int w = WigiDashProtocol.Width, h = WigiDashProtocol.Height;
        using var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.FromArgb(16, 18, 24));

            using var clock = new Font("Segoe UI", 96, FontStyle.Bold, GraphicsUnit.Pixel);
            using var small = new Font("Segoe UI", 28, GraphicsUnit.Pixel);
            using var dim = new SolidBrush(Color.FromArgb(140, 150, 170));
            using var center = new StringFormat { Alignment = StringAlignment.Center };

            g.DrawString(now.ToString("HH:mm"), clock, Brushes.White, new RectangleF(0, 170, w, 130), center);
            g.DrawString(reason, small, dim, new RectangleF(0, 320, w, 40), center);
            g.DrawString("OpenWigiDash", small, dim, new RectangleF(0, h - 70, w, 40), center);
        }

        var data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var source = new byte[data.Stride * h];
            Marshal.Copy(data.Scan0, source, 0, source.Length);
            var frame = new byte[WigiDashProtocol.FrameBytes];
            Rgb565.FromBgra32(source, w, h, data.Stride, frame);
            return frame;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}
