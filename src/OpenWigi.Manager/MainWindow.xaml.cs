using System;
using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using OpenWigi.Manager.Services;

namespace OpenWigi.Manager;

/// <summary>Janela do manager: só o hospedeiro do Blazor. Fechar esconde; o agente segue no tray.</summary>
public partial class MainWindow : Window
{
    private readonly IServiceProvider _services;

    public MainWindow(IServiceProvider services)
    {
        _services = services;
        InitializeComponent();
        WebView.Services = services;
        RestorePlacement();
    }

    // Abre onde e como foi fechada. Se o monitor de antes sumiu, fica no padrão (centralizada).
    private void RestorePlacement()
    {
        if (_services.GetRequiredService<ManagerSettings>().Window is not { } saved)
            return;
        var bounds = new Rect(saved.Left, saved.Top, Math.Max(MinWidth, saved.Width), Math.Max(MinHeight, saved.Height));
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!screen.IntersectsWith(bounds))
            return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        (Left, Top, Width, Height) = (bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        if (saved.Maximized)
            WindowState = WindowState.Maximized;
    }

    // RestoreBounds é o tamanho fora do maximizado: ao desmaximizar depois de abrir, volta para ele.
    private void SavePlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (bounds.IsEmpty)
            return;
        _services.GetRequiredService<ManagerSettings>().Window = new ManagerSettings.WindowPlacement(
            bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == WindowState.Maximized);
    }

    public void Navigate(string route) => _services.GetRequiredService<ManagerSettings>().RequestNavigation(route);

    private bool _exiting;

    /// <summary>Fecha de verdade (Sair no menu do tray).</summary>
    public void CloseForExit()
    {
        _exiting = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        SavePlacement();
        // Esconde em vez de fechar: o WebView2 é caro de recriar e o painel continua rodando.
        if (_exiting)
            return;
        e.Cancel = true;
        Hide();
    }
}
