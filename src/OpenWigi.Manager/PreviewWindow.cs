using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        root.Children.Add(new Border { Margin = new Thickness(8, 8, 8, 0), Background = Brushes.Black, Child = _image });
        Content = root;

        _agent.PreviewChanged += OnPreview;
        _agent.Changed += OnStatus;
        OnPreview();
        OnStatus();
        Closed += (_, _) =>
        {
            _agent.PreviewChanged -= OnPreview;
            _agent.Changed -= OnStatus;
        };
    }

    private void OnPreview()
    {
        if (_agent.Preview is not { } pixels)
            return;
        Dispatcher.BeginInvoke(() => _bitmap.WritePixels(
            new Int32Rect(0, 0, WigiDashProtocol.Width, WigiDashProtocol.Height), pixels, WigiDashProtocol.Width * 2, 0));
    }

    private void OnStatus() => Dispatcher.BeginInvoke(() => _status.Text = _agent.Reason);

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
