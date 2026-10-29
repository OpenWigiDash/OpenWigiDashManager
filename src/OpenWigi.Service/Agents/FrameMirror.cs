using OpenWigi.Device.Protocol;

namespace OpenWigi.Service.Agents;

/// <summary>
/// Cópia, na memória do serviço, do que foi escrito em cada página do aparelho. O WigiDash não
/// devolve a imagem, então é daqui que sai a prévia.
/// </summary>
public sealed class FrameMirror
{
    private readonly Lock _gate = new();
    private readonly byte[][] _pages = Enumerable.Range(0, WigiDashProtocol.PageCount)
        .Select(_ => new byte[WigiDashProtocol.FrameBytes]).ToArray();
    private int _shown = -1;
    private bool _dirty;

    public void Write(int page, byte[] pixels, int byteOffset)
    {
        if (page is < 0 or >= WigiDashProtocol.PageCount || byteOffset < 0 || byteOffset + pixels.Length > WigiDashProtocol.FrameBytes)
            return;
        lock (_gate)
        {
            pixels.CopyTo(_pages[page], byteOffset);
            _dirty |= page == _shown;
        }
    }

    public void Show(int page)
    {
        lock (_gate)
        {
            _dirty |= page != _shown;
            _shown = page;
        }
    }

    public void MarkDirty()
    {
        lock (_gate)
            _dirty = true;
    }

    /// <summary>Devolve a página na tela se ela mudou desde a última chamada.</summary>
    public (int Page, byte[] Pixels)? TakeIfChanged()
    {
        lock (_gate)
        {
            if (!_dirty || _shown < 0)
                return null;
            _dirty = false;
            return (_shown, (byte[])_pages[_shown].Clone());
        }
    }
}
