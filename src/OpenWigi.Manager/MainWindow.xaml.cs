using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using OpenWigi.Manager.Services;

namespace OpenWigi.Manager;

/// <summary>Janela do manager: só o hospedeiro do Blazor. Fechar esconde; o agente segue no tray.</summary>
public partial class MainWindow : Window
{
    private readonly IServiceProvider _services;
    private readonly ManagerSettings _settings;
    private readonly DispatcherTimer _splashFallback;
    private Microsoft.Web.WebView2.Wpf.WebView2CompositionControl? _webView;

    public MainWindow(IServiceProvider services)
    {
        _services = services;
        _settings = services.GetRequiredService<ManagerSettings>();
        // Fundo do WebView2 antes de a página carregar (por padrão é branco). Tem que estar definido antes
        // de o controle ser criado; vale para o processo todo, então acompanha o tema a cada janela nova.
        var background = (_settings.DarkMode ? Palette.Dark : Palette.Light).Background;
        Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "FF" + background.TrimStart('#'));
        InitializeComponent();
        ApplyTheme();
        WebView.Services = services;
        RestorePlacement();

        _settings.UiReady += OnUiReady;
        _settings.Changed += OnSettingsChanged;
        // Se o Blazor nunca avisar (erro na primeira renderização), a tela de abertura sai mesmo assim
        // para o aviso de erro do Blazor aparecer.
        _splashFallback = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _splashFallback.Tick += (_, _) => HideSplash();
        _splashFallback.Start();
        DarkTitleBar.Attach(this);
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                OnShownAgain();
        };
        // Minimizada o WebView2 também para de pintar; na volta, o mesmo cuidado da volta do tray.
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized)
                ShowCover();
            else
                OnShownAgain();
        };
        WebView.BlazorWebViewInitialized += (_, e) =>
        {
            _webView = e.WebView;
            ApplyWebViewBackground();
        };
        Closed += (_, _) =>
        {
            _splashFallback.Stop();
            _settings.UiReady -= OnUiReady;
            _settings.Changed -= OnSettingsChanged;
        };
    }

    // Cores da tela de abertura e da janela, iguais às do OpenWigiDashTheme (fundo, texto, primária, linhas).
    private static class Palette
    {
        public static readonly (string Background, string Text, string Accent, string Track) Dark =
            ("#121318", "#E8E9ED", "#E5243B", "#24262E");
        public static readonly (string Background, string Text, string Accent, string Track) Light =
            ("#F3F4F6", "#1B1D24", "#C8102E", "#DCDEE4");
    }

    private void ApplyTheme()
    {
        var colors = _settings.DarkMode ? Palette.Dark : Palette.Light;
        Background = Brush(colors.Background);
        Splash.Background = Brush(colors.Background);
        SplashTitle.Foreground = Brush(colors.Text);
        SplashProgress.Foreground = Brush(colors.Accent);
        SplashProgress.Background = Brush(colors.Track);
        Cover.Background = Brush(colors.Background);
        ApplyWebViewBackground();
    }

    // O que o WebView2 pinta enquanto não tem quadro da página (ao criar e ao voltar a ficar visível).
    private void ApplyWebViewBackground()
    {
        if (_webView is null)
            return;
        var hex = (_settings.DarkMode ? Palette.Dark : Palette.Light).Background;
        var color = (Color)ColorConverter.ConvertFromString(hex);
        _webView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, color.R, color.G, color.B);
    }

    private DispatcherTimer? _coverTimer;

    // Volta do tray: a capa (posta no OnClosing) segura a tela na cor do tema até o WebView2 retomar a
    // pintura, e some com um fade curto. Na primeira abertura quem cobre é a tela de abertura.
    private void OnShownAgain()
    {
        if (Cover.Visibility != Visibility.Visible)
            return;
        _coverTimer?.Stop();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _coverTimer = timer;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
            fade.Completed += (_, _) =>
            {
                if (IsVisible)
                    Cover.Visibility = Visibility.Collapsed;
            };
            Cover.BeginAnimation(OpacityProperty, fade);
        };
        timer.Start();
    }

    private void ShowCover()
    {
        _coverTimer?.Stop();
        Cover.BeginAnimation(OpacityProperty, null);
        Cover.Opacity = 1;
        Cover.Visibility = Visibility.Visible;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private void OnSettingsChanged() => Dispatcher.BeginInvoke(new Action(ApplyTheme));

    private void OnUiReady() => Dispatcher.BeginInvoke(new Action(() =>
    {
        // Um instante para o navegador pintar o que o Blazor acabou de mandar, e então some com um fade.
        var wait = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        wait.Tick += (_, _) =>
        {
            wait.Stop();
            HideSplash();
        };
        wait.Start();
    }));

    private void HideSplash()
    {
        _splashFallback.Stop();
        if (Splash.Visibility != Visibility.Visible || !Splash.IsHitTestVisible)
            return;
        Splash.IsHitTestVisible = false;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
        fade.Completed += (_, _) =>
        {
            Splash.Visibility = Visibility.Collapsed;
            SplashProgress.IsIndeterminate = false; // para a animação da barra
        };
        Splash.BeginAnimation(OpacityProperty, fade);
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
        ShowCover();
        Hide();
    }
}
