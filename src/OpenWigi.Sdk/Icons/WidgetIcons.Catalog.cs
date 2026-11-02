namespace OpenWigi.Sdk;

// A lista da biblioteca. Para crescer: acrescente no fim da categoria certa, com chave nova em minúsculas e hífens.
// Nunca mude a chave nem o desenho de um ícone que já saiu: ela está gravada nas configurações dos widgets.
// Glifos da Segoe MDL2 Assets, que a Segoe Fluent Icons repete nos mesmos códigos.
public static partial class WidgetIcons
{
    private static WidgetIconCategory[] BuildCategories() =>
    [
        new("audio", "Áudio"),
        new("camera", "Câmera e imagem"),
        new("devices", "Dispositivos"),
        new("media", "Mídia"),
        new("system", "Sistema"),
        new("communication", "Comunicação"),
        new("places", "Lugares e transporte"),
        new("symbols", "Símbolos e setas"),
    ];

    private static WidgetIcon[] BuildIcons() =>
    [
        // Áudio. As chaves e desenhos daqui vieram do plugin System (indicador de áudio) e não mudam.
        I("speaker", "Alto-falante", "audio", "", "som", "caixa de som", "speakers"),
        I("headphones", "Fone de ouvido", "audio", "", "headphone"),
        I("headset", "Headset", "audio", "", "fone com microfone", "gamer"),
        I("microphone", "Microfone", "audio", "", "mic", "voz"),
        I("microphone-off", "Microfone desligado", "audio", "", "mic", "mudo", "mute"),
        I("volume", "Volume", "audio", "", "som"),
        I("volume-high", "Volume alto", "audio", "", "som"),
        I("volume-medium", "Volume médio", "audio", "", "som"),
        I("volume-low", "Volume baixo", "audio", "", "som"),
        I("volume-zero", "Volume zero", "audio", "", "som"),
        I("mute", "Mudo", "audio", "", "silencio", "sem som"),
        I("music", "Música", "audio", "", "audio"),
        I("music-note", "Nota musical", "audio", "", "musica"),

        // Câmera e imagem
        I("camera", "Webcam", "camera", "", "video", "camera"),
        Combined("webcam-mic", "Webcam + microfone", "camera", "", "", "video", "camera", "mic"),
        I("photo-camera", "Câmera de foto", "camera", "", "foto"),
        I("picture", "Imagem", "camera", "", "foto", "figura"),
        I("photo", "Foto", "camera", "", "imagem"),

        // Dispositivos
        I("monitor", "Monitor / TV", "devices", "", "tela", "televisao", "display"),
        I("laptop", "Notebook", "devices", "", "laptop", "computador"),
        I("tablet", "Tablet", "devices", ""),
        I("phone", "Telefone", "devices", "", "ligacao", "chamada"),
        I("cellphone", "Celular", "devices", "", "smartphone", "telefone"),
        I("game", "Controle", "devices", "", "jogo", "gamepad", "xbox"),
        I("keyboard", "Teclado", "devices", "", "keyboard"),
        I("mouse", "Mouse", "devices", ""),
        I("bluetooth", "Bluetooth", "devices", "", "sem fio"),
        I("devices", "Dispositivos", "devices", "", "aparelhos"),
        I("hard-drive", "Disco", "devices", "", "hd", "ssd", "armazenamento"),
        I("printer", "Impressora", "devices", "", "imprimir"),
        I("processor", "Processamento", "devices", "", "cpu", "processador", "desempenho"),

        // Mídia
        I("play", "Tocar", "media", "", "play", "reproduzir"),
        I("pause", "Pausar", "media", "", "pause"),
        I("stop", "Parar", "media", "", "stop"),
        I("next", "Próxima", "media", "", "avancar", "pular", "next"),
        I("previous", "Anterior", "media", "", "voltar", "previous"),
        I("shuffle", "Aleatório", "media", "", "shuffle", "embaralhar"),
        I("repeat-all", "Repetir todas", "media", "", "repeat", "loop"),
        I("repeat-one", "Repetir uma", "media", "", "repeat", "loop"),
        I("record", "Gravar", "media", "", "rec", "gravacao"),

        // Sistema
        I("settings", "Configurações", "system", "", "engrenagem", "ajustes", "opcoes"),
        I("power", "Energia", "system", "", "desligar", "ligar", "power"),
        I("lock", "Bloquear", "system", "", "cadeado", "trancar"),
        I("unlock", "Desbloquear", "system", "", "cadeado", "destrancar"),
        I("refresh", "Atualizar", "system", "", "recarregar"),
        I("sync", "Sincronizar", "system", ""),
        I("download", "Baixar", "system", "", "download"),
        I("upload", "Enviar arquivo", "system", "", "upload", "subir"),
        I("search", "Procurar", "system", "", "busca", "lupa", "pesquisar"),
        I("home", "Início", "system", "", "casa", "home"),
        I("wifi", "Wi-Fi", "system", "", "rede", "internet", "sem fio"),
        I("ethernet", "Rede cabeada", "system", "", "rede", "cabo", "internet"),
        I("globe", "Internet", "system", "", "web", "mundo", "navegador"),
        I("cloud", "Nuvem", "system", ""),
        I("brightness", "Brilho", "system", "", "sol", "claridade"),
        I("brightness-low", "Brilho baixo", "system", "", "sol"),
        I("lightbulb", "Lâmpada", "system", "", "luz", "ideia"),
        I("folder", "Pasta", "system", "", "diretorio"),
        I("document", "Documento", "system", "", "arquivo", "pagina"),
        I("code", "Código", "system", "", "programacao", "dev"),
        I("fullscreen", "Tela cheia", "system", "", "maximizar"),
        I("back-to-window", "Sair da tela cheia", "system", "", "restaurar"),
        I("clock", "Relógio", "system", "", "hora", "tempo"),
        I("alarm", "Alarme", "system", "", "despertador"),
        I("timer", "Timer", "system", "", "temporizador", "contagem"),
        I("stopwatch", "Cronômetro", "system", "", "tempo"),
        I("calendar", "Calendário", "system", "", "agenda", "data"),
        I("save", "Salvar", "system", "", "disquete"),
        I("copy", "Copiar", "system", ""),
        I("paste", "Colar", "system", ""),
        I("cut", "Recortar", "system", "", "tesoura"),
        I("edit", "Editar", "system", "", "lapis"),
        I("delete", "Excluir", "system", "", "lixeira", "apagar"),

        // Comunicação
        I("mail", "E-mail", "communication", "", "email", "correio"),
        I("chat", "Conversa", "communication", "", "mensagem", "chat"),
        I("message", "Mensagem", "communication", "", "chat", "balao"),
        I("people", "Pessoas", "communication", "", "grupo", "contatos"),
        I("contact", "Contato", "communication", "", "pessoa", "perfil", "usuario"),
        I("send", "Enviar", "communication", "", "mandar"),
        I("link", "Link", "communication", "", "corrente", "url"),
        I("attach", "Anexo", "communication", "", "clipe"),
        I("ringer", "Notificação", "communication", "", "sino", "campainha", "alerta"),
        I("emoji", "Emoji", "communication", "", "sorriso", "carinha"),

        // Lugares e transporte
        I("location", "Localização", "places", "", "gps", "local"),
        I("map-pin", "Marcador no mapa", "places", "", "pino", "local"),
        I("car", "Carro", "places", "", "automovel"),
        I("bus", "Ônibus", "places", ""),
        I("train", "Trem", "places", "", "metro"),
        I("airplane", "Avião", "places", "", "voo", "viagem"),
        I("shop", "Loja", "places", "", "compras"),
        I("shopping-cart", "Carrinho", "places", "", "compras", "mercado"),

        // Símbolos e setas
        I("add", "Adicionar", "symbols", "", "mais", "plus"),
        I("remove", "Remover", "symbols", "", "menos", "minus"),
        I("cancel", "Cancelar", "symbols", "", "fechar", "x"),
        I("check", "Confirmar", "symbols", "", "ok", "visto", "check"),
        I("info", "Informação", "symbols", "", "info"),
        I("warning", "Aviso", "symbols", "", "atencao", "alerta"),
        I("error", "Erro", "symbols", "", "falha"),
        I("help", "Ajuda", "symbols", "", "duvida", "interrogacao"),
        I("star", "Estrela", "symbols", "", "favorito"),
        I("heart", "Coração", "symbols", "", "amor", "curtir"),
        I("like", "Curtir", "symbols", "", "joinha", "like"),
        I("flag", "Bandeira", "symbols", "", "marcar"),
        I("pin", "Fixar", "symbols", "", "alfinete"),
        I("lightning", "Raio", "symbols", "", "energia", "rapido"),
        I("arrow-up", "Seta para cima", "symbols", "", "subir"),
        I("arrow-down", "Seta para baixo", "symbols", "", "descer"),
        I("arrow-left", "Seta para a esquerda", "symbols", "", "voltar"),
        I("arrow-right", "Seta para a direita", "symbols", "", "avancar"),
    ];

    private static WidgetIcon I(string key, string name, string category, string glyph, params string[] tags) =>
        new(key, name, category, glyph) { Tags = tags };

    private static WidgetIcon Combined(string key, string name, string category, string glyph, string accent,
        params string[] tags) =>
        new(key, name, category, glyph, accent) { Tags = tags };
}
