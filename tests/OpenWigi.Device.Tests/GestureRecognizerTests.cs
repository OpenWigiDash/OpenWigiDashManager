using Microsoft.Extensions.Time.Testing;
using OpenWigi.Device.Gestures;
using OpenWigi.Device.Protocol;

namespace OpenWigi.Device.Tests;

public class GestureRecognizerTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(33);

    private readonly FakeTimeProvider _time = new();
    private readonly GestureRecognizer _recognizer;

    public GestureRecognizerTests()
    {
        _recognizer = new GestureRecognizer(new GestureOptions { ReportProgress = false }, _time);
        Feed(None); // o reconhecedor espera ver "sem contato" antes de aceitar toques
    }

    private static TouchSample None => new(TouchContact.None, 0, 0, 1, false);
    private static TouchSample Down(int x, int y) => new(TouchContact.Down, x, y, 1, false);
    private static TouchSample Up(int x, int y) => new(TouchContact.Up, x, y, 1, false);

    private List<GestureKind> Feed(params TouchSample[] samples)
    {
        var kinds = new List<GestureKind>();
        foreach (var sample in samples)
        {
            kinds.AddRange(_recognizer.Process(sample).Select(e => e.Kind));
            _time.Advance(Poll);
        }
        return kinds;
    }

    private List<GestureKind> Hold(int x, int y, TimeSpan duration)
    {
        var kinds = new List<GestureKind>();
        for (var held = TimeSpan.Zero; held < duration; held += Poll)
            kinds.AddRange(Feed(Down(x, y)));
        return kinds;
    }

    [Fact]
    public void Toque_rapido_vira_tap()
    {
        var kinds = Feed(Down(100, 100), Down(101, 100), Up(101, 100));
        Assert.Equal([GestureKind.PressStarted, GestureKind.PressEnded, GestureKind.Tap], kinds);
    }

    [Fact]
    public void Up_sem_down_lido_ainda_conta_como_tap()
    {
        Assert.Equal([GestureKind.Tap], Feed(Up(50, 60)));
    }

    [Fact]
    public void Segurar_parado_dispara_long_press_com_o_dedo_na_tela()
    {
        var whileHolding = Hold(200, 200, TimeSpan.FromMilliseconds(700));
        Assert.Contains(GestureKind.LongPress, whileHolding);
        Assert.DoesNotContain(GestureKind.PressEnded, whileHolding);

        var release = Feed(Up(200, 200));
        Assert.Equal([GestureKind.PressEnded], release); // nada de tap depois do long-press
    }

    [Fact]
    public void Up_repetido_depois_do_long_press_nao_vira_tap()
    {
        Hold(200, 200, TimeSpan.FromMilliseconds(700));
        var release = Feed(Up(200, 200), Up(200, 200), Up(200, 200), None);
        Assert.Equal([GestureKind.PressEnded], release);

        _time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Contains(GestureKind.Tap, Feed(Down(200, 200), Up(200, 200))); // o próximo toque ainda funciona
    }

    [Fact]
    public void Long_press_sai_uma_vez_so()
    {
        var kinds = Hold(200, 200, TimeSpan.FromSeconds(2));
        Assert.Single(kinds, k => k == GestureKind.LongPress);
    }

    [Fact]
    public void Long_press_dispara_mesmo_sem_leituras_novas_de_contato()
    {
        Feed(Down(10, 10));
        var kinds = new List<GestureKind>();
        for (var i = 0; i < 20; i++)
            kinds.AddRange(Feed(None)); // firmware que não repete o Down enquanto o dedo está parado
        Assert.Contains(GestureKind.LongPress, kinds);
    }

    [Fact]
    public void Tempo_de_long_press_e_configuravel()
    {
        var recognizer = new GestureRecognizer(new GestureOptions { LongPressDelay = TimeSpan.FromMilliseconds(300), ReportProgress = false }, _time);
        recognizer.Process(None);
        recognizer.Process(Down(0, 0));
        _time.Advance(TimeSpan.FromMilliseconds(310));
        Assert.Contains(recognizer.Process(Down(0, 0)), e => e.Kind == GestureKind.LongPress);
    }

    [Fact]
    public void Mexer_o_dedo_cancela_long_press_e_tap()
    {
        var kinds = Feed(Down(100, 100), Down(130, 100));
        kinds.AddRange(Hold(130, 100, TimeSpan.FromSeconds(1)));
        kinds.AddRange(Feed(Up(130, 100)));
        Assert.DoesNotContain(GestureKind.LongPress, kinds);
        Assert.DoesNotContain(GestureKind.Tap, kinds);
        Assert.Equal(GestureKind.PressEnded, kinds[^1]);
    }

    [Theory]
    [InlineData(300, 100, GestureKind.SwipeRight)]
    [InlineData(-100, 100, GestureKind.SwipeLeft)]
    [InlineData(100, 300, GestureKind.SwipeDown)]
    [InlineData(100, -100, GestureKind.SwipeUp)]
    public void Arrastar_longe_vira_swipe(int x, int y, GestureKind expected)
    {
        var kinds = Feed(Down(100, 100), Down((100 + x) / 2, (100 + y) / 2), Down(x, y));
        Assert.Contains(expected, kinds);
    }

    [Fact]
    public void Depois_do_swipe_espera_soltar_antes_de_novo_gesto()
    {
        Feed(Down(100, 100), Down(400, 100));
        Assert.Empty(Feed(Down(400, 100), Up(400, 100)));
        Feed(None);
        _time.Advance(TimeSpan.FromMilliseconds(350)); // e o intervalo pós-swipe
        Assert.Contains(GestureKind.PressStarted, Feed(Down(10, 10)));
    }

    [Fact]
    public void Dois_taps_perto_viram_double_tap()
    {
        var kinds = Feed(Down(100, 100), Up(100, 100));
        _time.Advance(TimeSpan.FromMilliseconds(200)); // passa o bloqueio pós-tap
        kinds.AddRange(Feed(Down(105, 102), Up(105, 102)));
        Assert.Equal(2, kinds.Count(k => k == GestureKind.Tap));
        Assert.Equal(GestureKind.DoubleTap, kinds[^1]);
    }

    [Fact]
    public void Leituras_logo_depois_do_tap_sao_ignoradas()
    {
        Feed(Down(100, 100), Up(100, 100));
        Assert.Empty(Feed(Up(100, 100))); // firmware repetindo o Up dentro do bloqueio
    }

    [Fact]
    public void Progresso_vai_de_zero_a_um_antes_do_long_press()
    {
        var recognizer = new GestureRecognizer(new GestureOptions(), _time);
        recognizer.Process(None);
        recognizer.Process(Down(0, 0));
        _time.Advance(TimeSpan.FromMilliseconds(300));
        var progress = Assert.Single(recognizer.Process(Down(0, 0)), e => e.Kind == GestureKind.PressProgress);
        Assert.Equal(0.5, progress.Progress, 2);
    }

    // Captura real no aparelho (2026-10-29): dois swipes para a esquerda seguidos; entre eles o dedo
    // voltando encostou 47 ms no meio da tela, ~260 ms depois de sair, e virava tap num botão.
    [Fact]
    public void Dedo_voltando_logo_depois_do_swipe_nao_vira_tap()
    {
        var swipe = Feed(Down(742, 310), Down(734, 313), Down(667, 319), Down(631, 319), Down(549, 315),
            Down(492, 312), Down(389, 305), Down(145, 312), Up(145, 312));
        Assert.Contains(GestureKind.SwipeLeft, swipe);

        Feed(None, None, None, None, None, None, None); // ~230 ms sem contato
        Assert.Empty(Feed(Down(552, 299), Up(552, 299), None));

        // O swipe seguinte, ~460 ms depois do primeiro sair, vale normalmente.
        _time.Advance(TimeSpan.FromMilliseconds(150));
        var next = Feed(Down(787, 373), Down(705, 372), Down(638, 370), Down(596, 369), Up(596, 369));
        Assert.Contains(GestureKind.SwipeLeft, next);
    }

    [Fact]
    public void Toque_depois_do_intervalo_do_swipe_vale()
    {
        Feed(Down(700, 300), Down(600, 300), Down(500, 300), Up(500, 300), None);
        _time.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Contains(GestureKind.Tap, Feed(Down(200, 70), Down(200, 70), Up(200, 70)));
    }
}