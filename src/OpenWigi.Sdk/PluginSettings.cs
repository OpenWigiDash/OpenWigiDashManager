using Microsoft.AspNetCore.Components;

namespace OpenWigi.Sdk;

/// <summary>
/// Tela de configurações do plugin inteiro: o que vale para todos os widgets dele (conta, conexão, chave de API).
/// O manager mostra uma vez só, na página do plugin, e não na tela de cada widget.
/// <code>[assembly: OpenWigiPluginSettings(typeof(MinhaConta))]</code>
/// O componente deriva de <see cref="PluginSettingsComponent"/>. Um por assembly.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class OpenWigiPluginSettingsAttribute(Type componentType) : Attribute
{
    /// <summary>Componente Razor (<see cref="IComponent"/>), normalmente derivado de <see cref="PluginSettingsComponent"/>.</summary>
    public Type ComponentType { get; } = componentType;
}

/// <summary>
/// Base para a tela de configurações do plugin. Não há instância: a tela abre mesmo sem nenhum widget do plugin no
/// layout. O host passa o <see cref="IWidgetHost"/> do plugin em cascata e carrega MudBlazor com o tema do manager.
/// Guarde o que for do plugin inteiro por conta própria (ex.: um arquivo em
/// <c>%LOCALAPPDATA%\OpenWigiDash\WidgetData\&lt;plugin&gt;</c>), não nas configurações de uma instância.
/// </summary>
public abstract class PluginSettingsComponent : ComponentBase
{
    [CascadingParameter]
    public IWidgetHost Host { get; set; } = default!;
}
