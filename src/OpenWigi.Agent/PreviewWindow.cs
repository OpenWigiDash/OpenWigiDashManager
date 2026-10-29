using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OpenWigi.Device.Protocol;

namespace OpenWigi.Agent;

/// <summary>
/// Janela de prévia: mostra o que o serviço mandou para o aparelho e transforma o mouse em toque.
/// Segurar o botão dá long-press; arrastar dá swipe, pelo mesmo reconhecedor do toque real.
/// </summary>
internal sealed class PreviewWindow : Form
{
    private readonly PictureBox _picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Normal };
    private bool _pressed;

    public PreviewWindow(string title)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(WigiDashProtocol.Width, WigiDashProtocol.Height);
        BackColor = Color.Black;
        Controls.Add(_picture);

        _picture.MouseDown += (_, e) => Press(TouchContact.Down, e);
        _picture.MouseMove += (_, e) => { if (_pressed) Press(TouchContact.Down, e); };
        _picture.MouseUp += (_, e) => Press(TouchContact.Up, e);
    }

    /// <summary>Toque emulado pronto para mandar ao serviço.</summary>
    public event Action<TouchContact, int, int>? Touch;

    public void ShowFrame(byte[] rgb565)
    {
        var bitmap = new Bitmap(WigiDashProtocol.Width, WigiDashProtocol.Height, PixelFormat.Format16bppRgb565);
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);
        try
        {
            var row = WigiDashProtocol.Width * 2;
            for (var y = 0; y < bitmap.Height; y++)
                Marshal.Copy(rgb565, y * row, data.Scan0 + y * data.Stride, row);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        BeginInvoke(() =>
        {
            var old = _picture.Image;
            _picture.Image = bitmap;
            old?.Dispose();
        });
    }

    public void SetStatus(string status) => BeginInvoke(() => Text = status);

    private void Press(TouchContact contact, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left && contact != TouchContact.Up)
            return;
        _pressed = contact == TouchContact.Down;
        var x = Math.Clamp(e.X, 0, WigiDashProtocol.Width - 1);
        var y = Math.Clamp(e.Y, 0, WigiDashProtocol.Height - 1);
        Touch?.Invoke(contact, x, y);
    }
}
