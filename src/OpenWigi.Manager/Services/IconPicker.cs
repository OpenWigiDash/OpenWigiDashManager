using System.Threading;
using System.Threading.Tasks;
using MudBlazor;
using OpenWigi.Manager.Components;
using OpenWigi.Sdk;

namespace OpenWigi.Manager.Services;

/// <summary>
/// A janela de ícones que o manager oferece às telas de configurações dos plugins (<see cref="IWidgetIconPicker"/>).
/// Abre pelo <see cref="IDialogService"/>, que desenha no <c>MudDialogProvider</c> da raiz, por cima do canvas.
/// </summary>
public sealed class IconPicker(IDialogService dialogs) : IWidgetIconPicker
{
    public async ValueTask<WidgetIconChoice?> PickAsync(WidgetIconPickerOptions options, CancellationToken cancellationToken = default)
    {
        var parameters = new DialogParameters<IconPickerDialog> { { d => d.Options, options } };
        var dialog = await dialogs.ShowAsync<IconPickerDialog>(options.Title, parameters,
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseOnEscapeKey = true, CloseButton = true });
        await using var cancel = cancellationToken.Register(() => dialog.Close());
        var result = await dialog.Result;
        return result is { Canceled: false, Data: WidgetIconChoice choice } ? choice : null;
    }
}
