using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using OpenWigi.Manager.Services;

namespace OpenWigi.Manager.Tray;

/// <summary>
/// Ícone da bandeja. O desenho é um mini WigiDash (tela larga com a grade de widgets) e o ponto no
/// canto diz o estado: vermelho = com a tela, cinza = tela em outra sessão, âmbar = sem aparelho ou serviço.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly AgentClient _agent;
    private Icon? _current;
    private AgentStatus? _drawn;

    private readonly Action _onChanged;

    /// <param name="post">Leva a atualização para a thread da UI (o agente avisa de outra thread).</param>
    public TrayIcon(AgentClient agent, Action<Action> post, Action open, Action openPreview, Action exit)
    {
        _agent = agent;
        _onChanged = () => post(Update);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Abrir o OpenWigiDash", null, (_, _) => open());
        menu.Items.Add("Prévia do aparelho", null, (_, _) => openPreview());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => exit());

        _icon = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                open();
        };
        Update();
        agent.Changed += _onChanged;
    }

    public void Dispose()
    {
        _agent.Changed -= _onChanged;
        _icon.Visible = false;
        _icon.Dispose();
        DestroyCurrent();
    }

    private void Update()
    {
        var status = _agent.Status;
        // Texto da dica: até 127 caracteres no Windows.
        var text = $"OpenWigiDash — {Describe(status)}";
        _icon.Text = text.Length > 127 ? text[..127] : text;
        if (_drawn == status)
            return;

        var previous = _current;
        _current = Render(status);
        _icon.Icon = _current;
        _drawn = status;
        if (previous is not null)
            DestroyIcon(previous.Handle);
    }

    private string Describe(AgentStatus status) => status switch
    {
        AgentStatus.Owner => "mostrando o seu painel",
        AgentStatus.Waiting => _agent.Reason,
        AgentStatus.NoDevice => "aparelho desconectado",
        _ => "serviço não encontrado",
    };

    internal static Icon Render(AgentStatus status)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // Corpo do aparelho: tela larga, moldura grafite.
            using var body = Rounded(new RectangleF(1, 6, 30, 20), 4);
            using var frame = new SolidBrush(Color.FromArgb(0x1B, 0x1D, 0x24));
            using var border = new Pen(Color.FromArgb(0x8B, 0x93, 0xA7), 1.5f);
            g.FillPath(frame, body);
            g.DrawPath(border, body);

            // Grade 3x2 de widgets, o primeiro em destaque.
            using var cell = new SolidBrush(Color.FromArgb(0x3A, 0x3E, 0x4A));
            using var accent = new SolidBrush(Color.FromArgb(0xE5, 0x24, 0x3B));
            for (var row = 0; row < 2; row++)
                for (var column = 0; column < 3; column++)
                    g.FillRectangle(row == 0 && column == 0 ? accent : cell, 5 + column * 8, 10 + row * 7, 6, 5);

            // Estado.
            var dot = status switch
            {
                AgentStatus.Owner => Color.FromArgb(0x3D, 0xD6, 0x8C),
                AgentStatus.Waiting => Color.FromArgb(0x8B, 0x93, 0xA7),
                _ => Color.FromArgb(0xF2, 0xB5, 0x44),
            };
            using var dotBrush = new SolidBrush(dot);
            using var dotBorder = new Pen(Color.FromArgb(0x12, 0x13, 0x18), 2);
            g.FillEllipse(dotBrush, 21, 19, 10, 10);
            g.DrawEllipse(dotBorder, 21, 19, 10, 10);
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void DestroyCurrent()
    {
        if (_current is not null)
            DestroyIcon(_current.Handle);
        _current = null;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
