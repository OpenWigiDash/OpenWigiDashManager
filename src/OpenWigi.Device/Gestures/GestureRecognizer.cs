using OpenWigi.Device.Protocol;

namespace OpenWigi.Device.Gestures;

public enum GestureKind
{
    /// <summary>Dedo encostou.</summary>
    PressStarted,
    /// <summary>Dedo segue parado antes do long-press; <see cref="GestureEvent.Progress"/> vai de 0 a 1.</summary>
    PressProgress,
    /// <summary>Dedo ficou parado pelo tempo de long-press. Sai com o dedo ainda na tela.</summary>
    LongPress,
    /// <summary>Dedo saiu (fecha todo <see cref="PressStarted"/>).</summary>
    PressEnded,
    Tap,
    /// <summary>Segundo toque perto do anterior; vem logo depois do <see cref="Tap"/> desse toque.</summary>
    DoubleTap,
    SwipeLeft,
    SwipeRight,
    SwipeUp,
    SwipeDown,
}

/// <summary>Um gesto reconhecido, em coordenadas da tela (0..1015, 0..591).</summary>
public readonly record struct GestureEvent(GestureKind Kind, int X, int Y, TimeSpan Duration, double Progress = 0);

public sealed record GestureOptions
{
    public TimeSpan LongPressDelay { get; init; } = TimeSpan.FromMilliseconds(600);
    /// <summary>Toque mais longo que isso (e que não virou long-press) não conta como tap.</summary>
    public TimeSpan TapTimeout { get; init; } = TimeSpan.FromMilliseconds(800);
    /// <summary>Depois de um tap, leituras são ignoradas por esse tempo (o firmware repete eventos).</summary>
    public TimeSpan TapLockout { get; init; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan DoubleTapWindow { get; init; } = TimeSpan.FromMilliseconds(400);
    public int DoubleTapDistance { get; init; } = 40;
    /// <summary>Movimento a partir do qual o toque vira arraste (cancela tap e long-press).</summary>
    public int DragThreshold { get; init; } = 15;
    /// <summary>Movimento a partir do qual o arraste vira swipe.</summary>
    public int SwipeThreshold { get; init; } = 150;
    public bool ReportProgress { get; init; } = true;
}

/// <summary>
/// Transforma as leituras cruas de toque (Down/Up/nada, a cada ~33 ms) em gestos.
/// Puro e síncrono: recebe a leitura e devolve os eventos; o relógio vem do <see cref="TimeProvider"/>.
/// </summary>
public sealed class GestureRecognizer(GestureOptions? options = null, TimeProvider? time = null)
{
    private readonly GestureOptions _options = options ?? new GestureOptions();
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    // Até ver "nada" uma vez, ignoramos leituras: na conexão e depois de um swipe o firmware
    // ainda pode estar reportando o contato antigo.
    private bool _waitForIdle = true;
    private bool _pressing;
    private bool _dragging;
    private bool _longFired;
    private long _pressStart;
    private int _startX, _startY, _lastX, _lastY;
    private long? _lastTapAt;
    private long? _lockoutFrom;
    private int _lastTapX, _lastTapY;

    public GestureOptions Options => _options;

    public IReadOnlyList<GestureEvent> Process(TouchSample sample)
    {
        var now = _time.GetTimestamp();
        var events = new List<GestureEvent>(2);

        if (_lockoutFrom is { } tapAt && !_pressing && _time.GetElapsedTime(tapAt, now) < _options.TapLockout)
            return events;

        if (_waitForIdle)
        {
            if (sample.Contact == TouchContact.None)
                _waitForIdle = false;
            return events;
        }

        switch (sample.Contact)
        {
            case TouchContact.Down when !_pressing:
                Start(sample, now, events);
                break;
            case TouchContact.Down:
                Move(sample, now, events);
                break;
            case TouchContact.Up:
                Release(sample, now, events);
                break;
        }

        if (_pressing)
            CheckLongPress(now, events);

        return events;
    }

    /// <summary>Esquece o toque em andamento (ex.: o aparelho reconectou ou trocou de dono).</summary>
    public void Reset()
    {
        _pressing = _dragging = _longFired = false;
        _waitForIdle = true;
        _lastTapAt = _lockoutFrom = null;
    }

    private void Start(TouchSample sample, long now, List<GestureEvent> events)
    {
        _pressing = true;
        _dragging = false;
        _longFired = false;
        _pressStart = now;
        _startX = _lastX = sample.X;
        _startY = _lastY = sample.Y;
        events.Add(new GestureEvent(GestureKind.PressStarted, sample.X, sample.Y, TimeSpan.Zero));
    }

    private void Move(TouchSample sample, long now, List<GestureEvent> events)
    {
        _lastX = sample.X;
        _lastY = sample.Y;
        if (_longFired)
            return;

        var dx = sample.X - _startX;
        var dy = sample.Y - _startY;
        var distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (distance > _options.SwipeThreshold)
        {
            var kind = Math.Abs(dx) > Math.Abs(dy)
                ? dx > 0 ? GestureKind.SwipeRight : GestureKind.SwipeLeft
                : dy > 0 ? GestureKind.SwipeDown : GestureKind.SwipeUp;
            var duration = _time.GetElapsedTime(_pressStart, now);
            events.Add(new GestureEvent(kind, _startX, _startY, duration));
            events.Add(new GestureEvent(GestureKind.PressEnded, sample.X, sample.Y, duration));
            _pressing = false;
            _waitForIdle = true;
        }
        else if (distance > _options.DragThreshold)
        {
            _dragging = true;
        }
    }

    private void Release(TouchSample sample, long now, List<GestureEvent> events)
    {
        if (!_pressing)
        {
            // O Down foi rápido demais para a leitura pegar: é um tap.
            EmitTap(sample.X, sample.Y, TimeSpan.Zero, now, events);
            return;
        }

        var duration = _time.GetElapsedTime(_pressStart, now);
        var (x, y) = sample.X == 0 && sample.Y == 0 ? (_lastX, _lastY) : (sample.X, sample.Y);
        _pressing = false;

        if (!_longFired && !_dragging && duration < _options.TapTimeout)
        {
            events.Add(new GestureEvent(GestureKind.PressEnded, x, y, duration));
            EmitTap(_startX, _startY, duration, now, events);
            return;
        }

        events.Add(new GestureEvent(GestureKind.PressEnded, x, y, duration));
        if (_dragging)
            _waitForIdle = true;
    }

    private void CheckLongPress(long now, List<GestureEvent> events)
    {
        if (_longFired || _dragging)
            return;

        var held = _time.GetElapsedTime(_pressStart, now);
        if (held >= _options.LongPressDelay)
        {
            _longFired = true;
            events.Add(new GestureEvent(GestureKind.LongPress, _startX, _startY, held, 1));
        }
        else if (_options.ReportProgress && held > TimeSpan.Zero)
        {
            events.Add(new GestureEvent(GestureKind.PressProgress, _startX, _startY, held,
                held / _options.LongPressDelay));
        }
    }

    private void EmitTap(int x, int y, TimeSpan duration, long now, List<GestureEvent> events)
    {
        events.Add(new GestureEvent(GestureKind.Tap, x, y, duration));
        _lockoutFrom = now;
        if (_lastTapAt is { } previous
            && _time.GetElapsedTime(previous, now) <= _options.DoubleTapWindow
            && Math.Abs(x - _lastTapX) <= _options.DoubleTapDistance
            && Math.Abs(y - _lastTapY) <= _options.DoubleTapDistance)
        {
            events.Add(new GestureEvent(GestureKind.DoubleTap, x, y, duration));
            _lastTapAt = null;
        }
        else
        {
            _lastTapAt = now;
            _lastTapX = x;
            _lastTapY = y;
        }
    }
}
