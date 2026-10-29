using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenWigi.Device.Protocol;
using OpenWigi.Manager.Services;

namespace OpenWigi.Manager;

/// <summary>
/// A prévia destacada: só a tela do aparelho, numa janela própria que pode ficar sempre por cima.
/// WPF puro (sem WebView): o RGB565 do serviço vai direto para um bitmap Bgr565. O mouse vira toque,
/// assim: clique = tap, segurar = long-press, arrastar = swipe.
/// </summary>
public sealed class PreviewWindow : Window
{
    private readonly AgentClient _agent;
    private readonly WriteableBitmap _bitmap = new(WigiDashProtocol.Width, WigiDashProtocol.Height, 96, 96, PixelFormats.Bgr565, null);
    private readonly Image _image;
    private readonly TextBlock _status;
    private readonly Border _screen;
    private bool _pressed;

    public PreviewWindow(AgentClient agent)
    {
        _agent = agent;
        Title = "OpenWigiDash — prévia";
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/openwigidash.ico"));
        Width = WigiDashProtocol.Width / 2 + 16;
        Height = WigiDashProtocol.Height / 2 + 72;
        MinWidth = 320;
        MinHeight = 240;
        Background = new SolidColorBrush(Color.FromRgb(0x12, 0x13, 0x18));

        _image = new Image { Source = _bitmap, Stretch = Stretch.Uniform, Cursor = Cursors.Hand };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _image.MouseLeftButtonDown += (_, e) => { _pressed = true; _image.CaptureMouse(); Touch(TouchContact.Down, e); };
        _image.MouseMove += (_, e) => { if (_pressed) Touch(TouchContact.Down, e); };
        _image.MouseLeftButtonUp += (_, e) => { _pressed = false; _image.ReleaseMouseCapture(); Touch(TouchContact.Up, e); };

        var onTop = new CheckBox
        {
            Content = "Sempre por cima",
            Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xCB, 0xD3)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        onTop.Checked += (_, _) => Topmost = true;
        onTop.Unchecked += (_, _) => Topmost = false;

        _status = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA4, 0xB0)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var bar = new DockPanel { Margin = new Thickness(8, 4, 8, 6) };
        DockPanel.SetDock(onTop, Dock.Right);
        bar.Children.Add(onTop);
        bar.Children.Add(_status);

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Bottom);
        root.Children.Add(bar);
        _screen = new Border { Margin = new Thickness(8, 8, 8, 0), Background = Brushes.Black, Child = _image };
        root.Children.Add(_screen);
        Content = root;

        _agent.PreviewChanged += OnPreview;
        _agent.Changed += OnStatus;
        _agent.Dashboard.PageChanged += OnStatus;
        OnPreview();
        OnStatus();
        Closed += (_, _) =>
        {
            _agent.PreviewChanged -= OnPreview;
            _agent.Changed -= OnStatus;
            _agent.Dashboard.PageChanged -= OnStatus;
        };
        // Abre já na proporção do aparelho (a altura da barra de baixo só é conhecida depois do layout).
        ContentRendered += (_, _) => Height = ActualHeight - _screen.ActualHeight + _screen.ActualWidth / Ratio;
    }

    private void OnPreview()
    {
        if (_agent.Preview is not { } pixels)
            return;
        Dispatcher.BeginInvoke(() => _bitmap.WritePixels(
            new Int32Rect(0, 0, WigiDashProtocol.Width, WigiDashProtocol.Height), pixels, WigiDashProtocol.Width * 2, 0));
    }

// A página da prévia é a do aparelho e a do editor: mostra qual é, junto com o estado da tela.
    private void OnStatus() => Dispatcher.BeginInvoke(() =>
    {
        var page = _agent.Dashboard.Layout.Pages.ElementAtOrDefault(_agent.Dashboard.CurrentPage)?.Name;
        _status.Text = page is null ? _agent.Reason : $"{page} · {_agent.Reason}";
    });

    private const double Ratio = (double)WigiDashProtocol.Width / WigiDashProtocol.Height;
    private const int WmSizing = 0x0214;
    private const int WmszLeft = 1, WmszRight = 2, WmszTop = 3, WmszTopLeft = 4, WmszTopRight = 5, WmszBottom = 6, WmszBottomLeft = 7;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(KeepRatio);
    }

    // Redimensionar mantém a proporção da tela do aparelho: a borda que a pessoa puxa manda,
    // a outra dimensão acompanha. Bordas da janela e a barra de baixo ficam de fora da conta.
    private IntPtr KeepRatio(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmSizing || _screen.ActualWidth <= 0)
            return IntPtr.Zero;

        var dpi = VisualTreeHelper.GetDpi(this);
        var chromeW = (ActualWidth - _screen.ActualWidth) * dpi.DpiScaleX;
        var chromeH = (ActualHeight - _screen.ActualHeight) * dpi.DpiScaleY;
        var rect = Marshal.PtrToStructure<NativeRect>(lParam);
        var edge = wParam.ToInt32();

        double screenW = rect.Right - rect.Left - chromeW, screenH = rect.Bottom - rect.Top - chromeH;
        if (edge is WmszTop or WmszBottom)
            screenW = screenH * Ratio;
        else
            screenH = screenW / Ratio;

        var width = (int)Math.Round(screenW + chromeW);
        var height = (int)Math.Round(screenH + chromeH);
        if (edge is WmszLeft or WmszTopLeft or WmszBottomLeft)
            rect.Left = rect.Right - width;
        else
            rect.Right = rect.Left + width;
        if (edge is WmszTop or WmszTopLeft or WmszTopRight)
            rect.Top = rect.Bottom - height;
        else
            rect.Bottom = rect.Top + height;

        Marshal.StructureToPtr(rect, lParam, false);
        handled = true;
        return new IntPtr(1);
    }

    // A imagem fica centralizada com Stretch.Uniform: converte o ponto do mouse para pixels do aparelho.
    private void Touch(TouchContact contact, MouseEventArgs e)
    {
        var point = e.GetPosition(_image);
        var scale = Math.Min(_image.ActualWidth / WigiDashProtocol.Width, _image.ActualHeight / WigiDashProtocol.Height);
        if (scale <= 0)
            return;
        var offsetX = (_image.ActualWidth - WigiDashProtocol.Width * scale) / 2;
        var offsetY = (_image.ActualHeight - WigiDashProtocol.Height * scale) / 2;
        var x = Math.Clamp((int)((point.X - offsetX) / scale), 0, WigiDashProtocol.Width - 1);
        var y = Math.Clamp((int)((point.Y - offsetY) / scale), 0, WigiDashProtocol.Height - 1);
        _ = _agent.TouchAsync(contact, x, y);
    }
}
