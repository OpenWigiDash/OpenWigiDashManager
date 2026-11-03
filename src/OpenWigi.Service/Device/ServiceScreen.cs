using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using OpenWigi.Device.Display;
using OpenWigi.Device.Protocol;
using OpenWigi.Ipc;
using OpenWigi.Service.Sessions;

namespace OpenWigi.Service.Device;

/// <summary>Uma sessão na lista da tela do serviço. <paramref name="Picture"/>: BGRA de <see cref="UserProfile"/>, ou null.</summary>
public sealed record ServiceScreenSession(string Name, WindowsSessionState State, bool Remote, byte[]? Picture);

/// <summary>
/// O que a tela do serviço mostra. <paramref name="Locked"/> põe o cadeado ao lado do motivo (PC bloqueado ou sem
/// usuário no console). Sem sessões, a tela fica só com o logo, o relógio e o motivo.
/// </summary>
public sealed record ServiceScreenModel(string Reason, bool Locked, DateTime Now, string Version,
    IReadOnlyList<ServiceScreenSession> Sessions);

/// <summary>Tela que o serviço mostra quando nenhuma sessão tem a posse: logo, relógio, motivo, sessões abertas e versão.</summary>
public static class ServiceScreen
{
    private const int W = WigiDashProtocol.Width, H = WigiDashProtocol.Height;

    /// <summary>Quantas sessões cabem lado a lado. Passando disso, a última vaga vira "+N".</summary>
    public const int MaxSessions = 4;

    private static readonly Color Background = Color.FromArgb(16, 18, 24);
    private static readonly Color Dim = Color.FromArgb(196, 203, 216);
    private static readonly Color Faint = Color.FromArgb(150, 158, 176);
    private static readonly Color Divider = Color.FromArgb(48, 53, 68);
    private static readonly Color Accent = Color.FromArgb(46, 196, 190);

    // Cores das iniciais de quem não tem foto, escolhidas pelo nome (sempre a mesma para o mesmo usuário).
    private static readonly Color[] AvatarColors =
    [
        Color.FromArgb(120, 62, 160), Color.FromArgb(40, 120, 110), Color.FromArgb(52, 92, 160),
        Color.FromArgb(160, 80, 60), Color.FromArgb(150, 110, 40), Color.FromArgb(70, 80, 105),
    ];

    // Imagem do GDI+ não aceita uso de duas threads ao mesmo tempo: o desenho do logo passa pelo lock.
    private static readonly Lazy<Image?> Logo = new(LoadLogo);
    private static readonly Lock LogoLock = new();

    public static byte[] Render(ServiceScreenModel model)
    {
        using var bitmap = RenderBitmap(model);
        var data = bitmap.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var source = new byte[data.Stride * H];
            Marshal.Copy(data.Scan0, source, 0, source.Length);
            var frame = new byte[WigiDashProtocol.FrameBytes];
            Rgb565.FromBgra32(source, W, H, data.Stride, frame);
            return frame;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>A tela em 32 bits, antes de virar RGB565 (os testes gravam em PNG para conferir o desenho).</summary>
    public static Bitmap RenderBitmap(ServiceScreenModel model)
    {
        var bitmap = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Background);

        using var clock = new Font("Segoe UI", 120, FontStyle.Bold, GraphicsUnit.Pixel);
        using var reason = new Font("Segoe UI", 30, GraphicsUnit.Pixel);
        using var title = new Font("Segoe UI Semibold", 22, GraphicsUnit.Pixel);
        using var tiny = new Font("Segoe UI", 18, GraphicsUnit.Pixel);
        var time = model.Now.ToString("HH:mm");

        if (model.Sessions.Count == 0)
        {
            // Sem lista: tudo centralizado, com o logo grande em cima.
            DrawLogo(g, new RectangleF(W / 2f - 52, 40, 104, 104));
            DrawCentered(g, "OpenWigiDash", title, Dim, W / 2f, 145);
            DrawCentered(g, time, clock, Color.White, W / 2f, 222);
            DrawReason(g, model, reason, 377);
        }
        else
        {
            DrawLogo(g, new RectangleF(20, 12, 64, 64));
            DrawText(g, "OpenWigiDash", title, Dim, 90, 35);
            DrawCentered(g, time, clock, Color.White, W / 2f, 86);
            DrawReason(g, model, reason, 220);
            DrawSessions(g, model.Sessions);
        }

        var version = $"OpenWigiDashManager {model.Version}";
        DrawText(g, version, tiny, Faint, W - 24 - Measure(g, version, tiny), H - 32);
        return bitmap;
    }

    private static void DrawReason(Graphics g, ServiceScreenModel model, Font font, float capTop)
    {
        if (!model.Locked)
        {
            DrawCentered(g, model.Reason, font, Dim, W / 2f, capTop);
            return;
        }

        // Cadeado do lado esquerdo da frase, os dois centralizados juntos.
        var size = font.Size * 1.05f;
        const float gap = 12;
        var width = size + gap + Measure(g, model.Reason, font);
        var x = (W - width) / 2;
        DrawLock(g, x, capTop - font.Size * 0.17f, size, Dim);
        DrawText(g, model.Reason, font, Dim, x + size + gap, capTop);
    }

    private static void DrawSessions(Graphics g, IReadOnlyList<ServiceScreenSession> sessions)
    {
        using (var line = new Pen(Divider, 1))
            g.DrawLine(line, W / 2f - 300, 272, W / 2f + 300, 272);
        using var caption = new Font("Segoe UI", 18, GraphicsUnit.Pixel);
        using var name = new Font("Segoe UI Semibold", 24, GraphicsUnit.Pixel);
        using var state = new Font("Segoe UI", 19, GraphicsUnit.Pixel);
        DrawCentered(g, "Sessões abertas neste PC", caption, Faint, W / 2f, 290);

        var shown = sessions.Count > MaxSessions ? sessions.Take(MaxSessions - 1).ToList() : sessions.ToList();
        var slots = shown.Count + (sessions.Count > MaxSessions ? 1 : 0);
        const float spacing = 236, cy = 378, r = 56;
        var x0 = W / 2f - spacing * (slots - 1) / 2;

        for (var i = 0; i < shown.Count; i++)
        {
            var s = shown[i];
            var cx = x0 + i * spacing;
            var inUse = s.State == WindowsSessionState.Active;
            DrawAvatar(g, s, cx, cy, r);
            if (inUse)
            {
                using var ring = new Pen(Accent, 3);
                g.DrawEllipse(ring, cx - r - 4, cy - r - 4, 2 * (r + 4), 2 * (r + 4));
            }
            if (s.Remote)
                DrawRemoteBadge(g, cx + r * 0.71f, cy + r * 0.71f);

            DrawCentered(g, s.Name, name, inUse ? Color.White : Dim, cx, 452, spacing - 14);
            DrawCentered(g, Describe(s), state, inUse && s.Remote ? Accent : Faint, cx, 486, spacing - 14);
        }

        if (slots > shown.Count)
        {
            var cx = x0 + shown.Count * spacing;
            using var fill = new SolidBrush(Divider);
            g.FillEllipse(fill, cx - r, cy - r, 2 * r, 2 * r);
            using var more = new Font("Segoe UI Semibold", r * 0.7f, GraphicsUnit.Pixel);
            DrawCentered(g, $"+{sessions.Count - shown.Count}", more, Dim, cx, cy - more.Size * 0.35f);
        }
    }

    private static string Describe(ServiceScreenSession s) => s.State switch
    {
        WindowsSessionState.Active => s.Remote ? "Ativa · remota" : "Ativa",
        WindowsSessionState.Locked => s.Remote ? "Bloqueada · remota" : "Bloqueada",
        _ => "Desconectada",
    };

    private static void DrawAvatar(Graphics g, ServiceScreenSession s, float cx, float cy, float r)
    {
        var box = new RectangleF(cx - r, cy - r, 2 * r, 2 * r);
        if (s.Picture is { Length: UserProfile.PictureBytes } picture)
        {
            // A foto vem quadrada; reduz para o tamanho do círculo e preenche o círculo com ela (borda suave).
            using var source = FromBgra(picture, UserProfile.PictureSize);
            var size = (int)Math.Ceiling(2 * r);
            using var scaled = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var sg = Graphics.FromImage(scaled))
            {
                sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                sg.DrawImage(source, new Rectangle(0, 0, size, size));
            }
            using var brush = new TextureBrush(scaled, WrapMode.Clamp);
            brush.TranslateTransform(box.X, box.Y);
            g.FillEllipse(brush, box);
            return;
        }

        using (var fill = new SolidBrush(AvatarColors[StableHash(s.Name) % AvatarColors.Length]))
            g.FillEllipse(fill, box);
        using var font = new Font("Segoe UI Semibold", r * 0.8f, GraphicsUnit.Pixel);
        DrawCentered(g, Initials(s.Name), font, Color.White, cx, cy - font.Size * 0.35f);
    }

    /// <summary>Selo de acesso remoto: um monitor pequeno num círculo, no canto da foto.</summary>
    private static void DrawRemoteBadge(Graphics g, float cx, float cy)
    {
        const float r = 16;
        using (var border = new SolidBrush(Background))
            g.FillEllipse(border, cx - r - 3, cy - r - 3, 2 * (r + 3), 2 * (r + 3));
        using (var fill = new SolidBrush(Accent))
            g.FillEllipse(fill, cx - r, cy - r, 2 * r, 2 * r);

        using var pen = new Pen(Background, 2);
        g.DrawRectangle(pen, cx - 8, cy - 7, 16, 10);
        g.DrawLine(pen, cx, cy + 3, cx, cy + 7);
        g.DrawLine(pen, cx - 5, cy + 8, cx + 5, cy + 8);
    }

    /// <summary>Cadeado desenhado em vetor, sem depender de fonte de ícones do Windows. (x, y) é o canto do quadrado.</summary>
    private static void DrawLock(Graphics g, float x, float y, float s, Color color)
    {
        var bodyWidth = s * 0.72f;
        var bodyHeight = s * 0.52f;
        var bodyX = x + (s - bodyWidth) / 2;
        var bodyY = y + s - bodyHeight;
        var stroke = Math.Max(2f, s * 0.11f);
        var radius = bodyWidth * 0.30f;
        var cx = x + s / 2;
        var top = y + s * 0.06f + stroke / 2;

        using var pen = new Pen(color, stroke) { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
        using (var shackle = new GraphicsPath())
        {
            shackle.AddLine(cx - radius, bodyY + 1, cx - radius, top + radius);
            shackle.AddArc(cx - radius, top, 2 * radius, 2 * radius, 180, 180);
            shackle.AddLine(cx + radius, top + radius, cx + radius, bodyY + 1);
            g.DrawPath(pen, shackle);
        }

        using var brush = new SolidBrush(color);
        using (var body = RoundedRectangle(new RectangleF(bodyX, bodyY, bodyWidth, bodyHeight), s * 0.09f))
            g.FillPath(brush, body);

        using var hole = new SolidBrush(Background);
        var k = s * 0.07f;
        var holeY = bodyY + bodyHeight * 0.30f;
        g.FillEllipse(hole, cx - k, holeY, 2 * k, 2 * k);
        g.FillRectangle(hole, cx - k * 0.45f, holeY + k, k * 0.9f, bodyHeight * 0.40f - k);
    }

    private static GraphicsPath RoundedRectangle(RectangleF rect, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void DrawLogo(Graphics g, RectangleF where)
    {
        if (Logo.Value is not { } logo)
            return;
        lock (LogoLock)
            g.DrawImage(logo, where);
    }

    private static Image? LoadLogo()
    {
        using var stream = typeof(ServiceScreen).Assembly.GetManifestResourceStream("OpenWigi.Service.Logo.png");
        if (stream is null)
            return null;
        // Copia para a imagem não depender do stream depois de fechado.
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private static Bitmap FromBgra(byte[] bgra, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var row = 0; row < size; row++)
                Marshal.Copy(bgra, row * size * 4, data.Scan0 + row * data.Stride, size * 4);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    internal static string Initials(string name)
    {
        var parts = name.Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => char.ToUpperInvariant(parts[0][0]).ToString(),
            _ => $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}",
        };
    }

    private static int StableHash(string text)
    {
        var hash = 17;
        foreach (var c in text.ToLowerInvariant())
            hash = unchecked(hash * 31 + c);
        return hash & int.MaxValue;
    }

    // Texto posicionado pelo topo das maiúsculas, para o layout não depender da folga que cada fonte põe acima.
    private static float CapOffset(Font font)
    {
        var family = font.FontFamily;
        var ascent = font.Size * family.GetCellAscent(font.Style) / family.GetEmHeight(font.Style);
        return ascent - font.Size * 0.70f;
    }

    private static float Measure(Graphics g, string text, Font font) =>
        g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic).Width;

    private static void DrawText(Graphics g, string text, Font font, Color color, float x, float capTop)
    {
        using var brush = new SolidBrush(color);
        g.DrawString(text, font, brush, x, capTop - CapOffset(font), StringFormat.GenericTypographic);
    }

    private static void DrawCentered(Graphics g, string text, Font font, Color color, float cx, float capTop, float maxWidth = W)
    {
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        var y = capTop - CapOffset(font);
        g.DrawString(text, font, brush, new RectangleF(cx - maxWidth / 2, y, maxWidth, font.Size * 1.5f), format);
    }
}
