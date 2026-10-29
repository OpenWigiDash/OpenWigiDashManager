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

    private sealed record Stored(bool DarkMode = true);

    public ManagerSettings()
    {
        try
        {
            _stored = File.Exists(_path) ? JsonSerializer.Deserialize<Stored>(File.ReadAllText(_path), Json) ?? new() : new();
        }
        catch (JsonException)
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

    /// <summary>Abre com o login do Windows, direto no tray (chave Run do usuário, sem admin).</summary>
    public bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --tray");
            else
                key.DeleteValue(RunValue, throwOnMissingValue: false);
            Changed?.Invoke();
        }
    }

    public event Action? Changed;

    /// <summary>O tray pediu para abrir numa tela específica.</summary>
    public event Action<string>? NavigationRequested;

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
