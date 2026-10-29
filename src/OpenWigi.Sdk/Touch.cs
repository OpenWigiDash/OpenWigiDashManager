namespace OpenWigi.Sdk;

/// <summary>
/// O que o dedo fez. Mesmos nomes e mesma ordem do <c>OpenWigi.Device.Gestures.GestureKind</c>:
/// o host repassa o gesto do reconhecedor sem tradução.
/// </summary>
public enum TouchKind
{
    /// <summary>Dedo encostou no widget.</summary>
    PressStarted,
    /// <summary>Dedo segue parado antes do long-press; <see cref="TouchEvent.Progress"/> vai de 0 a 1.</summary>
    PressProgress,
    /// <summary>Dedo ficou parado pelo tempo de long-press. Chega com o dedo ainda na tela.</summary>
    LongPress,
    /// <summary>Dedo saiu. Fecha todo <see cref="PressStarted"/>, inclusive o que virou swipe ou long-press.</summary>
    PressEnded,
    Tap,
    /// <summary>Segundo toque perto do anterior; chega logo depois do <see cref="Tap"/> desse toque.</summary>
    DoubleTap,
    SwipeLeft,
    SwipeRight,
    SwipeUp,
    SwipeDown,
}

/// <summary>
/// Um gesto dentro do widget. <see cref="X"/> e <see cref="Y"/> são relativos ao canto superior esquerdo
/// do widget (em tela cheia, da tela). Swipe vem com o ponto onde o dedo encostou.
/// </summary>
/// <param name="Duration">Tempo desde o <see cref="TouchKind.PressStarted"/>.</param>
/// <param name="Progress">Só em <see cref="TouchKind.PressProgress"/> (0..1) e <see cref="TouchKind.LongPress"/> (1).</param>
public readonly record struct TouchEvent(TouchKind Kind, int X, int Y, TimeSpan Duration, double Progress = 0)
{
    public bool IsSwipe => Kind is TouchKind.SwipeLeft or TouchKind.SwipeRight or TouchKind.SwipeUp or TouchKind.SwipeDown;

    /// <summary>Um toque "de ação" (o que no manager antigo era um ClickEvent).</summary>
    public bool IsGesture => Kind is not (TouchKind.PressStarted or TouchKind.PressProgress or TouchKind.PressEnded);
}
