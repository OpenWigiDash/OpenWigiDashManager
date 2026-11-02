using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using OpenWigi.Host.Plugins;
using OpenWigi.Sdk;

namespace OpenWigi.Manager.Services;

/// <summary>
/// Tile de aviso de um widget que não está funcionando, para o aparelho e a prévia: o mesmo do Canvas
/// (fundo listrado, contorno tracejado, o triângulo de alerta, o que houve, qual widget e de qual plugin).
/// Num tile pequeno as linhas de baixo saem primeiro; o alerta e o título ficam sempre.
/// </summary>
public static class ProblemTileRenderer
{
    // Warning do tema escuro (#F2B544): a tela do aparelho é sempre escura.
    private static readonly Color Warning = Color.FromArgb(0xF2, 0xB5, 0x44);
    private static readonly Color Background = Color.FromArgb(0x1B, 0x1D, 0x24);
    private static readonly Color Text = Color.FromArgb(0xE8, 0xE9, 0xED);

    public static WidgetFrame Render(ProblemTileRequest request)
    {
        var (w, h) = (request.Size.Width, request.Size.Height);
        using var bitmap = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Background);

            using (var stripes = new HatchBrush(HatchStyle.WideUpwardDiagonal, Color.FromArgb(28, Warning), Background))
                g.FillRectangle(stripes, 0, 0, w, h);
            using (var border = new Pen(Warning, Math.Max(1, Math.Min(w, h) / 60f)) { DashStyle = DashStyle.Dash })
                g.DrawRectangle(border, border.Width / 2, border.Width / 2, w - border.Width, h - border.Width);

            var pad = Math.Max(3, Math.Min(w, h) / 14);
            var titleSize = Math.Clamp(h / 8f, 9f, 22f);
            var lineSize = Math.Clamp(titleSize * 0.85f, 8f, 18f);
            using var titleFont = new Font("Segoe UI", titleSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var nameFont = new Font("Segoe UI Semibold", lineSize, FontStyle.Regular, GraphicsUnit.Pixel);
            using var pluginFont = new Font("Segoe UI", lineSize * 0.9f, FontStyle.Regular, GraphicsUnit.Pixel);
            var titleHeight = titleFont.GetHeight(g);
            var nameHeight = nameFont.GetHeight(g);
            var pluginHeight = pluginFont.GetHeight(g);

            // O que cabe na altura, de cima para baixo: ícone, título, widget, plugin.
            var room = h - 2 * pad;
            var icon = Math.Max(8f, Math.Min(room * 0.3f, Math.Min(w * 0.4f, 64f)));
            var gap = Math.Max(2f, titleSize * 0.25f);
            var showName = room >= icon + gap + titleHeight + nameHeight;
            var showPlugin = showName && room >= icon + gap + titleHeight + nameHeight + pluginHeight;
            if (room < icon + gap + titleHeight)
                icon = Math.Max(8f, room - gap - titleHeight);
            var total = icon + gap + titleHeight + (showName ? nameHeight : 0) + (showPlugin ? pluginHeight : 0);
            var y = pad + Math.Max(0, (room - total) / 2);

            DrawAlert(g, w / 2f, y, icon);
            y += icon + gap;

            using var format = new StringFormat(StringFormatFlags.NoWrap)
            {
                Alignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
            };
            using var warning = new SolidBrush(Warning);
            using var text = new SolidBrush(Text);
            using var dim = new SolidBrush(Color.FromArgb(210, Warning));
            g.DrawString(request.Title, titleFont, warning, new RectangleF(pad, y, w - 2 * pad, titleHeight), format);
            y += titleHeight;
            if (showName)
            {
                g.DrawString(request.Widget, nameFont, text, new RectangleF(pad, y, w - 2 * pad, nameHeight), format);
                y += nameHeight;
            }
            if (showPlugin)
                g.DrawString(request.Plugin, pluginFont, dim, new RectangleF(pad, y, w - 2 * pad, pluginHeight), format);
        }

        var data = bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new byte[data.Stride * h];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            return new WidgetFrame(request.Size, FramePixelFormat.Bgra32, pixels, data.Stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    // Triângulo de alerta com a exclamação, centrado em x, com o topo em y.
    private static void DrawAlert(Graphics g, float x, float y, float size)
    {
        var height = size * 0.88f;
        var top = y + (size - height) / 2;
        using var path = new GraphicsPath();
        path.AddPolygon(new[] { new PointF(x, top), new PointF(x + size / 2, top + height), new PointF(x - size / 2, top + height) });
        using var fill = new SolidBrush(Warning);
        g.FillPath(fill, path);

        using var mark = new SolidBrush(Background);
        var stroke = Math.Max(1.5f, size / 9);
        g.FillRectangle(mark, x - stroke / 2, top + height * 0.36f, stroke, height * 0.34f);
        g.FillEllipse(mark, x - stroke / 2, top + height * 0.76f, stroke, stroke);
    }
}
