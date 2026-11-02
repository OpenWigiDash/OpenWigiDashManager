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

        // Só a área de conteúdo do contador (dentro da moldura) é reenviada.
        var write = _sink.Writes.First();
        Assert.Equal((counter.Y + TileFrame.Default.Inset) * Compositor.RowBytes, write.Offset);
        Assert.True(write.Length <= (counter.Height - 2 * TileFrame.Default.Inset) * Compositor.RowBytes);

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
    public async Task Toque_no_espaco_entre_widgets_vai_para_o_mais_perto()
    {
        var host = await StartAsync();
        await host.ActivateAsync(true);
        var counter = host.Layout.AllPlacements.Single(p => p.WidgetId == Counter).Bounds;
        var clock = host.Layout.AllPlacements.Single(p => p.WidgetId == Clock).Bounds;
        Assert.Equal(clock.Right + 4, counter.X); // vizinhos, com os 4 px da grade entre eles

        // 2 px antes do contador (3 px depois do relógio): é do contador.
        await host.HandleGestureAsync(new GestureEvent(GestureKind.Tap, counter.X - 2, counter.Y + 10, TimeSpan.Zero));

        var settings = Directory.GetFiles(Path.Combine(Data, "instances"), "settings.json", SearchOption.AllDirectories);
        Assert.Contains(settings, f => File.ReadAllText(f).Contains("\"value\": \"1\""));
    }

    [Fact]
    public async Task Toque_na_folga_da_borda_da_tela_vai_para_o_widget_da_borda()
    {
        var host = await StartAsync();
        await host.ActivateAsync(true);
        var counter = host.Layout.AllPlacements.Single(p => p.WidgetId == Counter).Bounds;
        Assert.Equal(WidgetGrid.Area.Y, counter.Y); // na primeira linha, colado na folga de cima

        // Na beira da tela, 4 px acima da célula: fora da folga do toque, mas é a borda.
        await host.HandleGestureAsync(new GestureEvent(GestureKind.Tap, counter.X + 10, 0, TimeSpan.Zero));

        var settings = Directory.GetFiles(Path.Combine(Data, "instances"), "settings.json", SearchOption.AllDirectories);
        Assert.Contains(settings, f => File.ReadAllText(f).Contains("\"value\": \"1\""));
    }

    [Fact]
    public async Task Toque_duplo_so_vale_se_os_dois_toques_foram_no_mesmo_widget()
    {
        var host = await StartAsync();
        await host.ActivateAsync(true);
        var counter = host.Layout.AllPlacements.Single(p => p.WidgetId == Counter).Bounds;
        var clock = host.Layout.AllPlacements.Single(p => p.WidgetId == Clock).Bounds;

        // Primeiro toque no relógio, segundo no contador ao lado: o contador ganha só o toque simples.
        await host.HandleGestureAsync(new GestureEvent(GestureKind.Tap, clock.Right - 5, clock.Y + 10, TimeSpan.Zero));
        await host.HandleGestureAsync(new GestureEvent(GestureKind.Tap, counter.X + 5, counter.Y + 10, TimeSpan.Zero));
        await host.HandleGestureAsync(new GestureEvent(GestureKind.DoubleTap, counter.X + 5, counter.Y + 10, TimeSpan.Zero));
        var counterId = host.Layout.AllPlacements.Single(p => p.WidgetId == Counter).InstanceId;
        // O plugin vive no contexto dele: lê pelo nome da propriedade.
        var instance = host.GetSettings(counterId)!.Value.Instance;
        object? Read(string name) => instance.GetType().GetProperty(name)!.GetValue(instance);
        Assert.Equal(1, Read("Count"));
        Assert.NotEqual("double tap", Read("LastGesture"));

        // Dois toques no contador: aí sim o toque duplo chega.
        await host.HandleGestureAsync(new GestureEvent(GestureKind.Tap, counter.X + 5, counter.Y + 10, TimeSpan.Zero));
        await host.HandleGestureAsync(new GestureEvent(GestureKind.DoubleTap, counter.X + 5, counter.Y + 10, TimeSpan.Zero));
        Assert.Equal("double tap", Read("LastGesture"));
    }

    [Fact]
    public async Task Moldura_da_pagina_muda_a_area_de_conteudo_e_fica_gravada()
    {
        var host = await StartAsync();
        var counter = host.Layout.AllPlacements.Single(p => p.WidgetId == Counter);
        var context = host.GetSettings(counter.InstanceId)!.Value.Context;
        Assert.Equal(new PixelSize(198, 143), context.PixelSize); // padrão: sem folga, a célula inteira

        await host.SetPageFrameAsync(0, TileFrame.Framed);

        Assert.Equal(new PixelSize(188, 133), context.PixelSize);
        Assert.Equal(TileFrame.Framed, context.Frame);
        Assert.Equal(TileFrame.Framed, new LayoutStore(Path.Combine(Data, "layout.json")).Load().Pages[0].Frame);
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
        Assert.Equal(TileFrame.Default.ContentSize(WidgetSize.Wide.CellSpan), host.GetSettings(id)!.Value.Context.PixelSize);
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
        Assert.Equal(TileFrame.Default.ContentSize(WidgetSize.Small.CellSpan), frame.Size);
        lock (changed)
            Assert.Contains(id, changed);
        Assert.Null(host.FrameOf(Guid.NewGuid()));
    }

    [Fact]
    public async Task Relogio_1x1_desenha_no_tamanho_e_na_cor_de_fundo_escolhida()
    {
        var host = await StartAsync();
        var page = await host.AddPageAsync("Relógio");
        var id = (await host.AddWidgetAsync(Clock, page, WidgetSize.Mini))!.Value;

        var frame = host.FrameOf(id);
        Assert.NotNull(frame);
        Assert.Equal(TileFrame.Default.ContentSize(WidgetSize.Mini.CellSpan), frame.Size);

        // A cor vem da tela de configurações; o plugin está em outro contexto de carga, então vai por reflexão.
        var instance = host.GetSettings(id)!.Value.Instance;
        var set = instance.GetType().GetMethod("SetBackgroundAsync")!;
        await (Task)set.Invoke(instance, [new WidgetColor(255, 0, 0)])!;

        // Canto de cima à esquerda é fundo; Bgra32 guarda azul, verde, vermelho.
        static byte[] Corner(WidgetFrame? f) => f is null ? [] : f.Pixels.Span[..3].ToArray();
        for (var i = 0; i < 50 && !Corner(host.FrameOf(id)).SequenceEqual(new byte[] { 0, 0, 255 }); i++)
            await Task.Delay(20);
        Assert.Equal(new byte[] { 0, 0, 255 }, Corner(host.FrameOf(id)));
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
    public async Task Ao_abrir_guarda_copia_do_layout()
    {
        var host = await StartAsync(); // primeira vez: não havia layout, nada a copiar
        var store = new LayoutStore(Path.Combine(Data, "layout.json"));
        Assert.Empty(store.Backups());
        await host.AddPageAsync("Música");
        await host.DisposeAsync();

        await StartAsync();
        var copy = Assert.Single(store.Backups());
        Assert.Contains(store.Load().Pages, p => p.Name == "Música");
        Assert.Equal(File.ReadAllText(store.Path), File.ReadAllText(copy));
    }

    [Theory]
    [InlineData("""{ "grid": 99, "pages": [] }""", "versão mais nova")]
    [InlineData("isto não é json", "não é um layout")]
    [InlineData("""{ "grid": 2, "pages": [ { "name": "P", "widgets": [ { "size": "banana" } ] } ] }""", "defeito")]
    public async Task Layout_que_nao_abre_vai_para_o_lado_e_o_manager_abre_vazio_avisando(string json, string reason)
    {
        Directory.CreateDirectory(Data);
        var path = Path.Combine(Data, "layout.json");
        File.WriteAllText(path, json);

        var host = await StartAsync();

        Assert.NotNull(host.LayoutProblem);
        Assert.Contains(reason, host.LayoutProblem);
        var aside = Assert.Single(Directory.GetFiles(Path.Combine(Data, "backups"), "ilegivel-*.json"));
        Assert.Contains(aside, host.LayoutProblem);
        Assert.Equal(json, File.ReadAllText(aside)); // o arquivo original fica intacto
        Assert.Single(host.Layout.Pages);
        Assert.Contains("\"grid\": 2", File.ReadAllText(path)); // layout novo no lugar
    }

    [Fact]
    public async Task Layout_bom_abre_sem_aviso()
    {
        var host = await StartAsync();
        Assert.Null(host.LayoutProblem);
    }

    [Fact]
    public async Task Widget_fora_da_grade_sai_e_o_resto_do_layout_abre()
    {
        Directory.CreateDirectory(Data);
        File.WriteAllText(Path.Combine(Data, "layout.json"), $$"""
            { "grid": 2, "pages": [ { "name": "P", "widgets": [
              { "widgetId": "{{Counter}}", "column": 0, "row": 0, "size": "2x2" },
              { "widgetId": "{{Counter}}", "column": 9, "row": 0, "size": "2x2" }
            ] } ] }
            """);

        var host = await StartAsync();

        Assert.Null(host.LayoutProblem);
        var widget = Assert.Single(host.Layout.Pages[0].Widgets);
        Assert.Equal(0, widget.Column);
    }

    [Fact]
    public async Task Plugin_com_plugin_json_quebrado_vira_erro_e_os_outros_carregam()
    {
        var broken = Path.Combine(Plugins, "Quebrado");
        Directory.CreateDirectory(broken);
        File.WriteAllText(Path.Combine(broken, "plugin.json"), "{ isto não é json");

        var host = await StartAsync();

        Assert.Contains(host.PluginErrors, e => e.PluginDirectory == broken);
        Assert.Equal(2, host.AvailableWidgets.Count);
    }

    [Fact]
    public void Copias_do_layout_ficam_so_as_mais_novas()
    {
        var store = new LayoutStore(Path.Combine(Data, "layout.json"));
        var start = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
        Assert.Null(store.Backup(start)); // sem layout

        for (var i = 0; i < 5; i++)
        {
            store.Save(new DashboardLayout { Pages = [new LayoutPage { Name = $"P{i}" }] });
            Assert.NotNull(store.Backup(start.AddMinutes(i), keep: 3));
        }
        Assert.Null(store.Backup(start.AddMinutes(10), keep: 3)); // igual à última: não repete

        var backups = store.Backups();
        Assert.Equal(3, backups.Count);
        Assert.Contains("\"P4\"", File.ReadAllText(backups[0]));
        Assert.Contains("\"P2\"", File.ReadAllText(backups[2]));
    }

    [Fact]
    public async Task Catalogo_usa_a_previa_do_plugin_como_icone()
    {
        var host = await StartAsync();

        var clock = await host.GetCatalogIconAsync(Clock);
        Assert.NotNull(clock);
        // O menor tamanho do relógio é o 1x1.
        Assert.Equal(DashboardHost.PreviewPixelSize(WidgetSize.Mini), clock.Size);
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
        Assert.Equal("ClockSettings", host.GetSettings(clock)?.Component.Name);
        Assert.Null(host.GetSettings(Guid.NewGuid()));
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

    [Fact]
    public async Task Widget_que_funciona_nao_tem_problema()
    {
        var host = await StartAsync();

        Assert.All(host.Layout.AllPlacements, p => Assert.Null(host.ProblemOf(p.InstanceId)));
    }

    [Fact]
    public async Task Plugin_removido_aparece_como_nao_instalado_com_o_nome_que_tinha()
    {
        var first = await StartAsync();
        var placement = first.Layout.AllPlacements.First(p => p.WidgetId == Clock);
        var name = first.NameOf(placement.InstanceId);
        await first.DisposeAsync();

        // Outra pasta de plugins, vazia: como se o plugin tivesse sido removido.
        _host = new DashboardHost(new DashboardOptions { PluginsDirectory = Path.Combine(_root, "Vazia"), DataDirectory = Data },
            _sink, NullLoggerFactory.Instance);
        await _host.StartAsync();

        var problem = _host.ProblemOf(placement.InstanceId);
        Assert.NotNull(problem);
        Assert.Equal(WidgetProblemKind.NotInstalled, problem.Kind);
        Assert.Equal(name, problem.WidgetName);
        Assert.Equal("OpenWigi.Samples.Clock", problem.PluginFolder);
        Assert.Equal(name, _host.DisplayNameOf(placement.InstanceId));
    }

    [Fact]
    public async Task Plugin_que_nao_carrega_mais_mostra_o_motivo_no_widget()
    {
        var first = await StartAsync();
        var placement = first.Layout.AllPlacements.First(p => p.WidgetId == Clock);
        var name = first.NameOf(placement.InstanceId);
        await first.DisposeAsync();

        // Mesma pasta do plugin, mas com uma DLL que não carrega (como uma feita para outro SDK).
        var broken = Path.Combine(_root, "Quebrados", "OpenWigi.Samples.Clock");
        Directory.CreateDirectory(broken);
        await File.WriteAllBytesAsync(Path.Combine(broken, "OpenWigi.Samples.Clock.dll"), [1, 2, 3, 4]);
        _host = new DashboardHost(new DashboardOptions { PluginsDirectory = Path.Combine(_root, "Quebrados"), DataDirectory = Data },
            _sink, NullLoggerFactory.Instance);
        await _host.StartAsync();

        var problem = _host.ProblemOf(placement.InstanceId);
        Assert.NotNull(problem);
        Assert.Equal(WidgetProblemKind.PluginFailed, problem.Kind);
        Assert.False(string.IsNullOrWhiteSpace(problem.Reason));
        Assert.Equal(_host.PluginErrors.Single().Reason, problem.Reason);
        // O tile mostra qual widget e qual plugin eram, e o widget fica no layout para voltar quando o plugin voltar.
        Assert.Equal(name, problem.WidgetName);
        Assert.Equal("OpenWigi.Samples.Clock", problem.PluginFolder);
        Assert.Contains(_host.Layout.AllPlacements, p => p.InstanceId == placement.InstanceId && p.WidgetId == Clock);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("3.0.0.0", null)]
    [InlineData("2.0.0.0", "Loja")]
    [InlineData("4.0.0.0", "Atualize o OpenWigiDash")]
    [InlineData("3.9.0.0", "Atualize o OpenWigiDash")]
    public void Plugin_feito_para_outro_SDK_tem_mensagem_clara(string? referenced, string? expected)
    {
        var problem = PluginCatalog.SdkProblem(referenced is null ? null : Version.Parse(referenced));

        if (expected is null)
            Assert.Null(problem);
        else
            Assert.Contains(expected, problem);
    }

    [Fact]
    public async Task Restaurar_copia_exportada_traz_paginas_widgets_e_configuracoes()
    {
        var host = await StartAsync();
        var widget = host.Layout.Pages[0].Widgets[0];
        var folder = Path.Combine(Data, "instances", widget.InstanceId.ToString("D"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "nota.txt"), "configurado");
        await host.AddPageAsync("Música");
        var exported = Path.Combine(_root, "copia.zip");
        await host.ExportLayoutAsync(exported);

        // Depois da cópia: tira o widget (apaga os dados dele) e a página.
        await host.RemoveWidgetAsync(widget.InstanceId);
        await host.RemovePageAsync(1);
        Assert.False(Directory.Exists(folder));
        var changed = 0;
        host.LayoutChanged += () => changed++;

        await host.RestoreLayoutAsync(exported);

        Assert.Equal(["Página 1", "Música"], host.Layout.Pages.Select(p => p.Name));
        Assert.Contains(host.Layout.Pages[0].Widgets, w => w.InstanceId == widget.InstanceId);
        Assert.NotNull(host.NameOf(widget.InstanceId)); // instância criada de novo
        Assert.Equal("configurado", File.ReadAllText(Path.Combine(folder, "nota.txt")));
        Assert.Equal(1, changed);
        var saved = new LayoutStore(Path.Combine(Data, "layout.json")).Load();
        Assert.Equal(2, saved.Pages.Count);
        // O layout de antes (sem o widget e com uma página) virou cópia automática.
        var before = LayoutStore.Parse(File.ReadAllText(host.LayoutBackups()[0]));
        Assert.Single(before.Pages);
        Assert.DoesNotContain(before.AllPlacements, w => w.InstanceId == widget.InstanceId);
    }

    [Fact]
    public async Task Restaurar_copia_automatica_nao_apaga_dados_dos_widgets_que_saem()
    {
        var host = await StartAsync();
        await host.DisposeAsync();
        host = await StartAsync(); // agora há uma cópia automática com os widgets padrão
        var backup = Assert.Single(host.LayoutBackups());
        var widget = host.Layout.Pages[0].Widgets[0];
        var page = await host.AddPageAsync("Nova");
        var added = await host.AddWidgetAsync(widget.WidgetId, page);
        Assert.NotNull(added);
        var folder = Path.Combine(Data, "instances", added.Value.ToString("D"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "nota.txt"), "fica");

        await host.RestoreLayoutAsync(backup);

        Assert.Single(host.Layout.Pages);
        Assert.Null(host.NameOf(added.Value));
        Assert.True(File.Exists(Path.Combine(folder, "nota.txt")));
        Assert.NotNull(host.NameOf(widget.InstanceId));
    }

    [Fact]
    public async Task Restaurar_arquivo_que_nao_e_layout_nao_muda_nada()
    {
        var host = await StartAsync();
        await host.AddPageAsync("Fica");
        var bad = Path.Combine(_root, "ruim.json");
        File.WriteAllText(bad, "{ \"nada\": 1 }");

        await Assert.ThrowsAsync<InvalidDataException>(() => host.RestoreLayoutAsync(bad));

        Assert.Equal(2, host.Layout.Pages.Count);
        Assert.All(host.Layout.AllPlacements, p => Assert.NotNull(host.NameOf(p.InstanceId)));
    }

    [Fact]
    public async Task Restaurar_layout_da_grade_5x4_converte_para_10x8()
    {
        var host = await StartAsync();
        var old = Path.Combine(_root, "antigo.json");
        File.WriteAllText(old, $$"""
            { "pages": [ { "name": "Antiga", "widgets": [ { "widgetId": "{{Clock}}", "column": 1, "row": 1, "size": "2x1" } ] } ] }
            """);

        await host.RestoreLayoutAsync(old);

        var placement = Assert.Single(host.Layout.AllPlacements);
        Assert.Equal((2, 2), (placement.Column, placement.Row));
        Assert.Equal("Antiga", host.Layout.Pages[0].Name);
        Assert.Equal(0, host.CurrentPage);
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
