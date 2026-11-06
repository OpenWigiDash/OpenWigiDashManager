using System.Reflection;

[assembly: OpenWigi.Sdk.OpenWigiPluginSettings(typeof(OpenWigi.Sdk.Tests.CounterPluginSettings))]

namespace OpenWigi.Sdk.Tests;

/// <summary>Tela do plugin inteiro do <see cref="CounterWidget"/>: vale para todos os widgets do assembly.</summary>
public sealed class CounterPluginSettings : PluginSettingsComponent;

public class PluginSettingsTests
{
    [Fact]
    public void Assembly_declara_uma_tela_do_plugin()
    {
        var attribute = typeof(CounterWidget).Assembly.GetCustomAttribute<OpenWigiPluginSettingsAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal(typeof(CounterPluginSettings), attribute.ComponentType);
        Assert.True(typeof(Microsoft.AspNetCore.Components.IComponent).IsAssignableFrom(attribute.ComponentType));
    }

    [Fact]
    public void So_uma_tela_do_plugin_por_assembly()
    {
        var usage = typeof(OpenWigiPluginSettingsAttribute).GetCustomAttribute<AttributeUsageAttribute>()!;
        Assert.False(usage.AllowMultiple);
        Assert.Equal(AttributeTargets.Assembly, usage.ValidOn);
    }

    [Fact]
    public async Task Host_que_nao_sabe_abrir_a_pagina_do_plugin_devolve_false()
    {
        IWidgetContext context = new FakeWidgetContext(WidgetSize.Small);
        Assert.False(await context.OpenPluginSettingsAsync());
    }
}
