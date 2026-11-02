using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace OpenWigi.Manager.Services;

/// <summary>Preferências do manager deste usuário (tema, início com o Windows) e navegação pedida pelo tray.</summary>
public sealed class ManagerSettings
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "OpenWigiDash";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenWigiDash", "manager.json");
    private Stored _stored;

    private sealed record Stored(bool DarkMode = true, int? Brightness = null, bool ShowWidgetNames = true, bool StartWithWindows = true,
        WindowPlacement? Window = null, bool CheckPluginUpdates = true, DateTimeOffset? LastPluginUpdateCheck = null,
        bool CheckAppUpdates = true, bool BetaAppUpdates = false, DateTimeOffset? LastAppUpdateCheck = null, string? NotifiedAppUpdate = null,
        WidgetGrouping AddWidgetGrouping = WidgetGrouping.None);

    /// <summary>Onde a janela estava ao fechar: posição e tamanho fora do maximizado, e se estava maximizada.</summary>
    public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);

    /// <summary>Lugar da janela do manager, para ela abrir como foi fechada. null = nunca fechou, abre no padrão.</summary>
    public WindowPlacement? Window
    {
        get => _stored.Window;
        set
        {
            if (_stored.Window == value)
                return;
            _stored = _stored with { Window = value };
            Save();
        }
    }

    public ManagerSettings()
    {
        try
        {
            _stored = File.Exists(_path) ? JsonSerializer.Deserialize<Stored>(File.ReadAllText(_path), Json) ?? new() : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _stored = new();
        }
    }

    public bool DarkMode
    {
        get => _stored.DarkMode;
        set
        {
            _stored = _stored with { DarkMode = value };
            Save();
            Changed?.Invoke();
        }
    }

    /// <summary>A loja confere uma vez por dia, em segundo plano, se os plugins instalados têm versão nova.</summary>
    public bool CheckPluginUpdates
    {
        get => _stored.CheckPluginUpdates;
        set
        {
            _stored = _stored with { CheckPluginUpdates = value };
            Save();
            Changed?.Invoke();
        }
    }

    /// <summary>Quando a última checagem de atualização dos plugins terminou.</summary>
    public DateTimeOffset? LastPluginUpdateCheck
    {
        get => _stored.LastPluginUpdateCheck;
        set
        {
            _stored = _stored with { LastPluginUpdateCheck = value };
            Save();
        }
    }

    /// <summary>O manager confere uma vez por dia, no GitHub, se há versão nova do OpenWigiDash.</summary>
    public bool CheckAppUpdates
    {
        get => _stored.CheckAppUpdates;
        set
        {
            _stored = _stored with { CheckAppUpdates = value };
            Save();
            Changed?.Invoke();
        }
    }

    /// <summary>Avisa também das versões de teste (pré-release: beta, rc).</summary>
    public bool BetaAppUpdates
    {
        get => _stored.BetaAppUpdates;
        set
        {
            _stored = _stored with { BetaAppUpdates = value };
            Save();
            Changed?.Invoke();
        }
    }

    /// <summary>Quando a última checagem de versão nova do OpenWigiDash terminou.</summary>
    public DateTimeOffset? LastAppUpdateCheck
    {
        get => _stored.LastAppUpdateCheck;
        set
        {
            _stored = _stored with { LastAppUpdateCheck = value };
            Save();
        }
    }

    /// <summary>Tag da última versão avisada no tray, para o balão não repetir a cada checagem.</summary>
    public string? NotifiedAppUpdate
    {
        get => _stored.NotifiedAppUpdate;
        set
        {
            if (_stored.NotifiedAppUpdate == value)
                return;
            _stored = _stored with { NotifiedAppUpdate = value };
            Save();
        }
    }

    /// <summary>Como o "Adicionar widget" agrupa os widgets; fica o último escolhido.</summary>
    public WidgetGrouping AddWidgetGrouping
    {
        get => _stored.AddWidgetGrouping;
        set
        {
            if (_stored.AddWidgetGrouping == value)
                return;
            _stored = _stored with { AddWidgetGrouping = value };
            Save();
            Changed?.Invoke();
        }
    }

    /// <summary>Nome dos widgets por cima da prévia no editor. Desligado, o tile mostra só o que vai para o aparelho.</summary>
    public bool ShowWidgetNames
    {
        get => _stored.ShowWidgetNames;
        set
        {
            _stored = _stored with { ShowWidgetNames = value };
            Save();
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Brilho que este usuário escolheu, reaplicado sempre que a tela volta para ele. null = nunca mexeu,
    /// fica o que o aparelho estiver usando.
    /// </summary>
    public int? Brightness
    {
        get => _stored.Brightness;
        set
        {
            if (_stored.Brightness == value)
                return;
            _stored = _stored with { Brightness = value };
            Save();
        }
    }

    /// <summary>
    /// Abre com o login do Windows, direto no tray. Instalado, o instalador já põe a entrada Run da
    /// máquina (<c>--autostart</c>) para todos os usuários, e aqui só se guarda se este usuário quer;
    /// sem ela (dev), vale a chave Run do usuário, sem admin.
    /// </summary>
    public bool StartWithWindows
    {
        get
        {
            if (MachineAutostart)
                return _stored.StartWithWindows;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            if (MachineAutostart)
            {
                _stored = _stored with { StartWithWindows = value };
                Save();
                RemoveUserRunEntry();
            }
            else
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value)
                    key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --tray");
                else
                    key.DeleteValue(RunValue, throwOnMissingValue: false);
            }
            Changed?.Invoke();
        }
    }

    /// <summary>O instalador registrou o início com o Windows para todos os usuários (HKLM).</summary>
    public static bool MachineAutostart
    {
        get
        {
            using var key = Registry.LocalMachine.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
    }

    /// <summary>
    /// Com a entrada da máquina, a do usuário (de antes do instalador, às vezes apontando para uma
    /// build de dev) abriria uma segunda cópia no login. Ela sai, e a escolha dela vira a preferência.
    /// </summary>
    public void AdoptMachineAutostart()
    {
        if (!MachineAutostart)
            return;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        if (key?.GetValue(RunValue) is not string)
            return;
        RemoveUserRunEntry();
        if (!_stored.StartWithWindows)
        {
            _stored = _stored with { StartWithWindows = true };
            Save();
        }
    }

    private static void RemoveUserRunEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(RunValue, throwOnMissingValue: false);
    }

    public event Action? Changed;

    /// <summary>O tray pediu para abrir numa tela específica.</summary>
    public event Action<string>? NavigationRequested;

    /// <summary>A UI pediu a prévia numa janela própria (o app abre, na thread do WPF).</summary>
    public event Action? PreviewDetachRequested;

    public void RequestPreviewWindow() => PreviewDetachRequested?.Invoke();

    /// <summary>O layout do Blazor desenhou pela primeira vez: a janela tira a tela de abertura.</summary>
    public event Action? UiReady;

    public void NotifyUiReady() => UiReady?.Invoke();

    /// <summary>Rota pedida antes de a janela terminar de carregar; o layout consome ao iniciar.</summary>
    public string? PendingRoute { get; set; }

    public void RequestNavigation(string route)
    {
        if (NavigationRequested is null)
            PendingRoute = route;
        else
            NavigationRequested(route);
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(_stored, Json));
    }
}

/// <summary>Agrupamento da lista do "Adicionar widget".</summary>
public enum WidgetGrouping
{
    /// <summary>Uma lista só.</summary>
    None,

    /// <summary>Um grupo por plugin de origem.</summary>
    Plugin,

    /// <summary>Por categoria do widget. Ainda não existe no SDK: fica para quando os plugins declararem.</summary>
    Category,
}
