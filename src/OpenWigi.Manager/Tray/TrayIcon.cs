using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using OpenWigi.Manager.Services;

namespace OpenWigi.Manager.Tray;

/// <summary>
/// Ícone da bandeja (assets/branding/tray). O ponto no canto diz o estado: verde = com a tela,
/// cinza = tela em outra sessão, âmbar = sem aparelho ou serviço. Há uma versão para a barra de
/// tarefas escura e outra para a clara, trocada quando o usuário muda o tema do Windows.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly NotifyIcon _icon;
    private readonly AgentClient _agent;
    private readonly Action _onChanged;
    private readonly UserPreferenceChangedEventHandler _onTheme;
    private readonly Dictionary<string, Icon> _cache = [];
    private string? _shown;

    /// <param name="post">Leva a atualização para a thread da UI (o agente avisa de outra thread).</param>
    public TrayIcon(AgentClient agent, Action<Action> post, Action open, Action openPreview, Action exit)
    {
        _agent = agent;
        _onChanged = () => post(Update);
        _onTheme = (_, e) =>
        {
            if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
                post(Update);
        };

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
        SystemEvents.UserPreferenceChanged += _onTheme;
    }

    public void ShowBalloon(string text) => _icon.ShowBalloonTip(5000, "OpenWigiDash", text, ToolTipIcon.Warning);

    public void Dispose()
    {
        _agent.Changed -= _onChanged;
        SystemEvents.UserPreferenceChanged -= _onTheme;
        _icon.Visible = false;
        _icon.Dispose();
        foreach (var icon in _cache.Values)
            icon.Dispose();
        _cache.Clear();
    }

    private void Update()
    {
        var status = _agent.Status;
        // Texto da dica: até 127 caracteres no Windows.
        var text = $"OpenWigiDash — {Describe(status)}";
        _icon.Text = text.Length > 127 ? text[..127] : text;

        var name = ResourceName(status, TaskbarIsLight());
        if (name == _shown)
            return;
        _icon.Icon = Load(name);
        _shown = name;
    }

    private string Describe(AgentStatus status) => status switch
    {
        AgentStatus.Owner => "mostrando o seu painel",
        AgentStatus.Waiting => _agent.Reason,
        AgentStatus.NoDevice => "aparelho desconectado",
        _ => "serviço não encontrado",
    };

    internal static string ResourceName(AgentStatus status, bool light)
    {
        var state = status switch
        {
            AgentStatus.Owner => "owner",
            AgentStatus.Waiting => "waiting",
            _ => "nodevice",
        };
        return $"tray-{state}-{(light ? "light" : "dark")}.ico";
    }

    // O NotifyIcon escolhe o tamanho certo (16, 20, 24, 32) de dentro do .ico conforme o DPI.
    private Icon Load(string name)
    {
        if (_cache.TryGetValue(name, out var cached))
            return cached;
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream($"OpenWigi.Manager.Tray.{name}")
            ?? throw new InvalidOperationException($"Ícone da bandeja não embutido: {name}");
        var icon = new Icon(stream, SystemInformation.SmallIconSize);
        _cache[name] = icon;
        return icon;
    }

    /// <summary>Barra de tarefas clara? (SystemUsesLightTheme = 1). Na dúvida, escura.</summary>
    private static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
    }
}
