using Microsoft.Extensions.Logging.Abstractions;
using OpenWigi.Device.Gestures;
using OpenWigi.Device.Protocol;
using OpenWigi.Host.Display;
using OpenWigi.Host.Layout;
using OpenWigi.Host.Plugins;
using OpenWigi.Sdk;

namespace OpenWigi.Host.Tests;

public sealed class HostTests : IAsyncLifetime
{
    private static readonly Guid Clock = new("4f0f9a5e-2a7b-4c11-9a51-3c1d7a9e2b10");
    private static readonly Guid Counter = new("9c3b1e47-6d20-4a8e-b5f4-2e7c0a91d6f3");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "openwigi-host-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeSink _sink = new();
    private DashboardHost? _host;

    private string Plugins => Path.Combine(_root, "Widgets");
    private string Data => Path.Combine(_root, "Data");

    public Task InitializeAsync()
    {
        // O exemplo é compilado junto (referência sem saída); copiamos como uma instalação de plugin.
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name; // Debug ou Release
        var repo = AppContext.BaseDirectory[..AppContext.BaseDirectory.IndexOf($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal)];
        var sample = Path.Combine(repo, "samples", "OpenWigi.Samples.Clock", "bin", configuration, "net10.0-windows");
        var target = Path.Combine(Plugins, "OpenWigi.Samples.Clock");
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(sample, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(sample, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A DLL do plugin pode seguir presa até o contexto ser coletado.
        }
    }

    private async Task<DashboardHost> StartAsync()
    {
        _host = new DashboardHost(new DashboardOptions { PluginsDirectory = Plugins, DataDirectory = Data }, _sink, NullLoggerFactory.Instance);
        await _host.StartAsync();
        return _host;
    }

    [Fact]
    public async Task Plugin_de_exemplo_carrega_os_dois_widgets_num_contexto_proprio()
    {
        var host = await StartAsync();

        Assert.Empty(host.PluginErrors);
        Assert.Equal(new HashSet<Guid> { Clock, Counter }, host.AvailableWidgets.Select(w => w.Manifest.Id).ToHashSet());
        var context = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(host.AvailableWidgets[0].Widget.GetType().Assembly);
        Assert.NotEqual(System.Runtime.Loader.AssemblyLoadContext.Default, context);
        Assert.True(context!.IsCollectible);

        // O contrato vem do host, não de uma cópia do plugin.
        Assert.Same(typeof(IWidget).Assembly, host.AvailableWidgets[0].Widget.GetType().GetInterface(nameof(IWidget))!.Assembly);
    }

    [Fact]
    public async Task Plugin_do_usuario_ganha_do_que_veio_com_o_instalador()
    {
        // O mesmo plugin nas duas pastas: carrega uma vez só, sem erro de id repetido.
        var bundled = Path.Combine(_root, "Bundled");
        CopyDirectory(Path.Combine(Plugins, "OpenWigi.Samples.Clock"), Path.Combine(bundled, "OpenWigi.Samples.Clock"));
        _host = new DashboardHost(new DashboardOptions { PluginsDirectory = Plugins, BundledPluginsDirectory = bundled, DataDirectory = Data },
            _sink, NullLoggerFactory.Instance);
        await _host.StartAsync();

        Assert.Empty(_host.PluginErrors);
        Assert.Equal(2, _host.AvailableWidgets.Count);
        Assert.All(_host.AvailableWidgets, w => Assert.StartsWith(Plugins, w.PluginDirectory, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Sem_widgets_do_usuario_carrega_os_do_instalador()
    {
        var bundled = Path.Combine(_root, "Bundled");
        Directory.Move(Plugins, bundled);
        _host = new DashboardHost(new DashboardOptions { PluginsDirectory = Plugins, BundledPluginsDirectory = bundled, DataDirectory = Data },
            _sink, NullLoggerFactory.Instance);
        await _host.StartAsync();

        Assert.Empty(_host.PluginErrors);
        Assert.Equal(new HashSet<Guid> { Clock, Counter }, _host.AvailableWidgets.Select(w => w.Manifest.Id).ToHashSet());
    }

    [Fact]
    public async Task Instalacao_pendente_entra_antes_de_carregar_e_pastas_com_ponto_sao_ignoradas()
    {
        // Como o owd plugin deixa um plugin que estava em uso: pasta nova em .pending, aplicada no início.
        var staged = OpenWigi.Plugins.PendingChanges.StagingDirectory(Plugins, "OpenWigi.Samples.Clock");
        Directory.CreateDirectory(OpenWigi.Plugins.PendingChanges.PendingDirectory(Plugins));
        Directory.Move(Path.Combine(Plugins, "OpenWigi.Samples.Clock"), staged);
        Directory.CreateDirectory(Path.Combine(Plugins, ".cache", "lixo"));

        var host = await StartAsync();

        Assert.Empty(host.PluginErrors);
        Assert.Equal(2, host.AvailableWidgets.Count);
        Assert.True(Directory.Exists(Path.Combine(Plugins, "OpenWigi.Samples.Clock")));
        Assert.False(Directory.Exists(OpenWigi.Plugins.PendingChanges.PendingDirectory(Plugins)));
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    [Fact]
    public async Task Primeira_execucao_monta_um_layout_com_cada_widget()
    {
        var host = await StartAsync();

        var widgets = host.Layout.Pages.Single().Widgets;
        Assert.Equal(2, widgets.Count);
        Assert.True(File.Exists(Path.Combine(Data, "layout.json")));
        Assert.DoesNotContain(widgets, a => widgets.Any(b => a != b && a.Overlaps(b.Column, b.Row, b.Size)));
    }

    [Fact]
    public async Task Ganhar_a_tela_manda_a_pagina_inteira_e_mostra()
    {
        var host = await StartAsync();
        await host.ActivateAsync(true);

        var write = Assert.Single(_sink.Writes);
        Assert.Equal((0, WigiDashProtocol.FrameBytes), (write.Offset, write.Length));
        Assert.Equal(write.Page, _sink.Shown.Single());
        Assert.NotEqual(2, write.Page); // página 2 é do serviço
    }

    [Fact]
    public async Task Tap_no_contador_soma_e_so_as_linhas_dele_vao_para_o_aparelho()
    {
        var host = await StartAsync();
        await host.ActivateAsync(true);
        _sink.Clear();

        var counter = host.Layout.AllPlacements.Single(p => p.WidgetId == Counter).Bounds;
        await host.HandleGestureAsync(new GestureEvent(GestureKind.Tap, counter.X + 10, counter.Y + 10, TimeSpan.FromMilliseconds(80)));
        await WaitForWriteAsync();

        var write = _sink.Writes.First();
        Assert.Equal(counter.Y * Compositor.RowBytes, write.Offset);
        Assert.True(write.Length <= counter.Height * Compositor.RowBytes);

        var settings = Directory.GetFiles(Path.Combine(Data, "instances"), "settings.json", SearchOption.AllDirectories);
        Assert.Contains(settings, f => File.ReadAllText(f).Contains("\"value\": \"1\""));
    }

    [Fact]
    public async Task Long_press_vai_para_o_widget_onde_o_dedo_encostou()
    {
        var host = await StartAsync();
        await host.ActivateAsync(true);
        var counter = host.Layout.AllPlacements.Single(p => p.WidgetId == Counter).Bounds;

        await host.HandleGestureAsync(new GestureEvent(GestureKind.Tap, counter.X + 5, counter.Y + 5, TimeSpan.Zero));
        await host.HandleGestureAsync(new GestureEvent(GestureKind.PressStarted, counter.X + 5, counter.Y + 5, TimeSpan.Zero));
        await host.HandleGestureAsync(new GestureEvent(GestureKind.LongPress, counter.X + 5, counter.Y + 5, TimeSpan.FromMilliseconds(600), 1));
        await host.HandleGestureAsync(new GestureEvent(GestureKind.PressEnded, counter.X + 5, counter.Y + 5, TimeSpan.FromMilliseconds(700)));

        var settings = Directory.GetFiles(Path.Combine(Data, "instances"), "settings.json", SearchOption.AllDirectories);
        Assert.Contains(settings, f => File.ReadAllText(f).Contains("\"value\": \"0\""));
    }

    [Fact]
    public async Task Swipe_troca_de_pagina_usando_a_outra_pagina_do_hardware()
    {
        // Duas páginas: uma com o relógio, outra com o contador.
        Directory.CreateDirectory(Data);
        var layout = new DashboardLayout
        {
            Pages =
            [
                new LayoutPage { Name = "A", Widgets = [new WidgetPlacement { WidgetId = Clock, Size = WidgetSize.Wide }] },
                new LayoutPage { Name = "B", Widgets = [new WidgetPlacement { WidgetId = Counter, Size = WidgetSize.Small }] },
            ],
        };
        new LayoutStore(Path.Combine(Data, "layout.json")).Save(layout);

        var host = await StartAsync();
        await host.ActivateAsync(true);
        var first = _sink.Shown.Last();
        var pageChanges = 0;
        host.PageChanged += () => pageChanges++;

        await host.HandleGestureAsync(new GestureEvent(GestureKind.SwipeLeft, 500, 300, TimeSpan.FromMilliseconds(200)));

        // O editor e a prévia acompanham pela notificação.
        Assert.Equal(1, host.CurrentPage);
        Assert.Equal(1, pageChanges);
        Assert.NotEqual(first, _sink.Shown.Last());
        Assert.Equal(Transition.WipeEast, _sink.Transitions.Last());
    }

    [Fact]
    public async Task Sem_a_tela_nada_vai_para_o_aparelho()
    {
        var host = await StartAsync();
        await host.ActivateAsync(true);
        await host.ActivateAsync(false);
        _sink.Clear();

        var counter = host.Layout.AllPlacements.Single(p => p.WidgetId == Counter).Bounds;
        await host.HandleGestureAsync(new GestureEvent(GestureKind.Tap, counter.X + 10, counter.Y + 10, TimeSpan.Zero));
        await Task.Delay(150);
        Assert.Empty(_sink.Writes);
    }

    [Fact]
    public async Task Editar_o_layout_grava_e_respeita_a_grade()
    {
        var host = await StartAsync();
        var changes = 0;
        host.LayoutChanged += () => changes++;

        var added = await host.AddWidgetAsync(Counter, 0);
        Assert.NotNull(added);
        Assert.True(await host.MoveWidgetAsync(added.Value, 4, 3));
        Assert.False(await host.MoveWidgetAsync(added.Value, 0, 0)); // ocupado pelo relógio
        Assert.False(await host.MoveWidgetAsync(added.Value, 5, 0)); // fora da grade

        await host.RemoveWidgetAsync(added.Value);
        Assert.DoesNotContain(host.Layout.AllPlacements, p => p.InstanceId == added.Value);
        Assert.False(Directory.Exists(Path.Combine(Data, "instances", added.Value.ToString("D"))));

        var page = await host.AddPageAsync("Jogos");
        await host.RenamePageAsync(page, "Música");
        Assert.Equal("Música", new LayoutStore(Path.Combine(Data, "layout.json")).Load().Pages[page].Name);
        Assert.True(await host.RemovePageAsync(page));
        Assert.False(await host.RemovePageAsync(0)); // página com widgets não sai
        Assert.True(changes >= 6);
    }

    [Fact]
    public async Task Redimensionar_recria_a_instancia_e_mantem_as_configuracoes()
    {
        var host = await StartAsync();
        var counter = host.Layout.AllPlacements.Single(p => p.WidgetId == Counter);
        var id = counter.InstanceId;
        await host.ActivateAsync(true);
        var bounds = counter.Bounds;
        await host.HandleGestureAsync(new GestureEvent(GestureKind.Tap, bounds.X + 5, bounds.Y + 5, TimeSpan.Zero)); // conta 1

        Assert.False(await host.ResizeWidgetAsync(id, WidgetSize.Large)); // contador não suporta 2x2
        var free = host.Layout.Pages[0].FindFreeCell(WidgetSize.Wide)!.Value;
        Assert.True(await host.MoveWidgetAsync(id, free.Column, free.Row));
        Assert.True(await host.ResizeWidgetAsync(id, WidgetSize.Wide));

        var resized = host.Layout.AllPlacements.Single(p => p.InstanceId == id);
        Assert.Equal(WidgetSize.Wide, resized.Size);
        var settings = Directory.GetFiles(Path.Combine(Data, "instances", id.ToString("D")), "settings.json");
        Assert.Contains("\"value\": \"1\"", File.ReadAllText(Assert.Single(settings)));
        Assert.Equal(WidgetSize.Wide.PixelSize, host.GetSettings(id)!.Value.Context.PixelSize);
    }

    [Fact]
    public async Task Instancias_do_mesmo_widget_ganham_nomes_distintos_e_editaveis()
    {
        var host = await StartAsync();
        var page = await host.AddPageAsync("Outra");
        var first = await host.AddWidgetAsync(Counter, page);
        var second = await host.AddWidgetAsync(Counter, page);

        // A primeira execução já pôs um contador no layout: estes são o 2 e o 3.
        Assert.Equal("Contador (exemplo) 2", host.DisplayNameOf(first!.Value));
        Assert.Equal("Contador (exemplo) 3", host.DisplayNameOf(second!.Value));

        await host.RenameWidgetAsync(second.Value, "  Contador do café ");
        Assert.Equal("Contador do café", host.DisplayNameOf(second.Value));
        Assert.Equal("Contador do café", new LayoutStore(Path.Combine(Data, "layout.json")).Load()
            .AllPlacements.Single(p => p.InstanceId == second.Value).Name);

        await host.RenameWidgetAsync(second.Value, "");
        Assert.Equal("Contador (exemplo)", host.DisplayNameOf(second.Value));
    }

    [Fact]
    public async Task Quadro_de_cada_instancia_fica_disponivel_para_o_editor()
    {
        var host = await StartAsync();
        var page = await host.AddPageAsync("Outra");
        var changed = new List<Guid>();
        host.FrameChanged += id => { lock (changed) changed.Add(id); };

        var id = (await host.AddWidgetAsync(Counter, page))!.Value;

        // Mesmo numa página que não está no aparelho, o editor já tem o quadro do tamanho do widget.
        var frame = host.FrameOf(id);
        Assert.NotNull(frame);
        Assert.Equal(WidgetSize.Small.PixelSize, frame.Size);
        lock (changed)
            Assert.Contains(id, changed);
        Assert.Null(host.FrameOf(Guid.NewGuid()));
    }

[Fact]
    public async Task Paginas_nunca_tem_nome_repetido()
    {
        // Layout antigo com nome repetido: ao carregar, a segunda ganha número.
        Directory.CreateDirectory(Data);
        new LayoutStore(Path.Combine(Data, "layout.json")).Save(new DashboardLayout
        {
            Pages = [new LayoutPage { Name = "Página 1" }, new LayoutPage { Name = "Página 3" }, new LayoutPage { Name = "página 3" }],
        });
        var host = await StartAsync();
        Assert.Equal(["Página 1", "Página 3", "página 3 2"], host.Layout.Pages.Select(p => p.Name));

        // Nova página sem nome pega o primeiro "Página N" livre; com nome usado, ganha número.
        Assert.Equal("Página 2", host.Layout.Pages[await host.AddPageAsync()].Name);
        Assert.Equal("Página 1 2", host.Layout.Pages[await host.AddPageAsync("Página 1")].Name);

        // Renomear para o nome de outra (sem diferenciar maiúsculas) é recusado; para o próprio, não.
        Assert.False(await host.RenamePageAsync(0, " página 3 "));
        Assert.Equal("Página 1", host.Layout.Pages[0].Name);
        Assert.True(await host.RenamePageAsync(0, "PÁGINA 1"));
        Assert.False(await host.RenamePageAsync(0, "  "));
    }

    [Fact]
    public async Task Excluir_pagina_com_widgets_so_com_aceite_e_apaga_as_instancias()
    {
        var host = await StartAsync();
        var page = await host.AddPageAsync("Jogos");
        var counter = (await host.AddWidgetAsync(Counter, page))!.Value;
        await host.GoToPageAsync(page, Transition.None);

        Assert.False(await host.RemovePageAsync(page)); // sem o aceite, não sai
        Assert.True(await host.RemovePageAsync(page, withWidgets: true));

        Assert.Equal(["Página 1"], host.Layout.Pages.Select(p => p.Name));
        Assert.Equal(0, host.CurrentPage); // estava nela: foi para a vizinha
        Assert.DoesNotContain(host.Layout.AllPlacements, p => p.InstanceId == counter);
        Assert.Null(host.FrameOf(counter));
        Assert.False(Directory.Exists(Path.Combine(Data, "instances", counter.ToString("D"))));
        Assert.False(await host.RemovePageAsync(0, withWidgets: true)); // a última página fica
    }

    [Fact]
    public async Task Mudar_a_ordem_das_paginas_mantem_a_pagina_atual()
    {
        var host = await StartAsync();
        await host.AddPageAsync("B");
        await host.AddPageAsync("C");
        await host.GoToPageAsync(2, Transition.None); // "C" no aparelho

        Assert.True(await host.MovePageAsync(2, 0));
        Assert.Equal(["C", "Página 1", "B"], host.Layout.Pages.Select(p => p.Name));
        Assert.Equal(0, host.CurrentPage); // continua mostrando "C", agora no lugar 0
        Assert.Equal(["C", "Página 1", "B"], new LayoutStore(Path.Combine(Data, "layout.json")).Load().Pages.Select(p => p.Name));
        Assert.False(await host.MovePageAsync(0, 3));
    }

    [Fact]
    public async Task Trocar_widgets_do_mesmo_tamanho_de_lugar()
    {
        var host = await StartAsync();
        var page = await host.AddPageAsync("Troca");
        var first = (await host.AddWidgetAsync(Counter, page))!.Value;
        var second = (await host.AddWidgetAsync(Counter, page))!.Value;
        var widgets = host.Layout.Pages[page].Widgets;
        var a = widgets.Single(w => w.InstanceId == first);
        var b = widgets.Single(w => w.InstanceId == second);
        var (aAt, bAt) = ((a.Column, a.Row), (b.Column, b.Row));

        Assert.True(await host.SwapWidgetsAsync(first, second));
        Assert.Equal(bAt, (a.Column, a.Row));
        Assert.Equal(aAt, (b.Column, b.Row));
        var saved = new LayoutStore(Path.Combine(Data, "layout.json")).Load().Pages[page].Widgets;
        Assert.Equal(bAt, saved.Single(w => w.InstanceId == first) is var s ? (s.Column, s.Row) : default);

        Assert.False(await host.SwapWidgetsAsync(first, first));
        Assert.False(await host.SwapWidgetsAsync(first, Guid.NewGuid()));
    }

    [Fact]
    public async Task Ao_abrir_de_novo_volta_na_pagina_em_que_estava()
    {
        var host = await StartAsync();
        await host.AddPageAsync("B");
        await host.AddPageAsync("C");
        await host.GoToPageAsync(2, Transition.None);
        Assert.True(await host.MovePageAsync(2, 1)); // a ordem muda, a página continua "C"
        await host.DisposeAsync();

        host = await StartAsync();
        Assert.Equal("C", host.Layout.Pages[host.CurrentPage].Name);

        // Página gravada que não existe mais: abre na primeira.
        await host.DisposeAsync();
        var store = new LayoutStore(Path.Combine(Data, "layout.json"));
        var layout = store.Load();
        layout.CurrentPageId = Guid.NewGuid();
        store.Save(layout);
        host = await StartAsync();
        Assert.Equal(0, host.CurrentPage);
    }

    [Fact]
    public async Task Catalogo_usa_a_previa_do_plugin_como_icone()
    {
        var host = await StartAsync();

        var clock = await host.GetCatalogIconAsync(Clock);
        Assert.NotNull(clock);
        Assert.Equal(WidgetSize.Small.PixelSize, clock.Size);
        Assert.Same(clock, await host.GetCatalogIconAsync(Clock));

        // O contador não implementa GetPreviewAsync: a UI mostra o ícone genérico.
        Assert.Null(await host.GetCatalogIconAsync(Counter));
        Assert.Null(await host.GetCatalogIconAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Tela_de_configuracoes_do_plugin_chega_ao_manager()
    {
        var host = await StartAsync();
        var counter = host.Layout.AllPlacements.Single(p => p.WidgetId == Counter).InstanceId;
        var clock = host.Layout.AllPlacements.Single(p => p.WidgetId == Clock).InstanceId;

        var settings = host.GetSettings(counter);
        Assert.NotNull(settings);
        Assert.Equal("CounterSettings", settings.Value.Component.Name);
        Assert.Null(host.GetSettings(clock));
    }

    [Fact]
    public void Plugin_sem_dll_de_entrada_vira_erro_e_nao_derruba_o_resto()
    {
        var broken = Path.Combine(Plugins, "Quebrado");
        Directory.CreateDirectory(broken);
        Assert.Null(PluginCatalog.FindEntry(broken));
    }

    private async Task WaitForWriteAsync()
    {
        for (var i = 0; i < 50 && _sink.Writes.Count == 0; i++)
            await Task.Delay(20);
    }

    private sealed class FakeSink : IDisplaySink
    {
        private readonly Lock _gate = new();
        private readonly List<(int Page, int Offset, int Length)> _writes = [];
        private readonly List<int> _shown = [];
        private readonly List<Transition> _transitions = [];

        public IReadOnlyList<(int Page, int Offset, int Length)> Writes { get { lock (_gate) return _writes.ToList(); } }
        public IReadOnlyList<int> Shown { get { lock (_gate) return _shown.ToList(); } }
        public IReadOnlyList<Transition> Transitions { get { lock (_gate) return _transitions.ToList(); } }

        public ValueTask WriteAsync(int page, int byteOffset, ReadOnlyMemory<byte> rgb565, CancellationToken cancellationToken)
        {
            lock (_gate)
                _writes.Add((page, byteOffset, rgb565.Length));
            return ValueTask.CompletedTask;
        }

        public ValueTask ShowAsync(int page, Transition transition, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _shown.Add(page);
                _transitions.Add(transition);
            }
            return ValueTask.CompletedTask;
        }

        public void Clear()
        {
            lock (_gate)
            {
                _writes.Clear();
                _shown.Clear();
                _transitions.Clear();
            }
        }
    }
}
