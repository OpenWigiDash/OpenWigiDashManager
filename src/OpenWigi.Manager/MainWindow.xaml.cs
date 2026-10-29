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
        // Esconde em vez de fechar: o WebView2 é caro de recriar e o painel continua rodando.
        if (_exiting)
            return;
        e.Cancel = true;
        Hide();
    }
}
