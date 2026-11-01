namespace OpenWigi.Plugins;

/// <summary>
/// Trocas de plugin que esperam o plugin sair de uso. DLL carregada fica travada no Windows, então a
/// instalação monta a pasta nova em <c>Widgets\.pending\&lt;id&gt;</c> e a remoção grava
/// <c>Widgets\.pending\&lt;id&gt;.remove</c>. <see cref="Apply"/> roda antes de carregar os plugins (e logo
/// depois de instalar, se o plugin não estiver aberto).
/// </summary>
public static class PendingChanges
{
    public const string FolderName = ".pending";
    private const string RemoveSuffix = ".remove";
    private const string OldSuffix = ".old";

    public static string PendingDirectory(string widgetsDirectory) => Path.Combine(widgetsDirectory, FolderName);
    public static string StagingDirectory(string widgetsDirectory, string id) => Path.Combine(PendingDirectory(widgetsDirectory), id);
    public static string RemoveMarker(string widgetsDirectory, string id) => Path.Combine(PendingDirectory(widgetsDirectory), id + RemoveSuffix);

    /// <summary>Ids com troca ainda pendente (instalar/atualizar ou remover).</summary>
    public static IReadOnlyList<(string Id, bool Remove)> List(string widgetsDirectory)
    {
        var pending = PendingDirectory(widgetsDirectory);
        if (!Directory.Exists(pending))
            return [];
        return Directory.GetDirectories(pending).Select(Path.GetFileName)
            .Where(n => !n!.EndsWith(OldSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(n => (n!, false))
            .Concat(Directory.GetFiles(pending, "*" + RemoveSuffix).Select(f => (Path.GetFileName(f)[..^RemoveSuffix.Length], true)))
            .ToList();
    }

    /// <summary>
    /// Aplica o que estiver pendente. Cada falha (pasta em uso, sem permissão) vira um item do retorno e a
    /// troca fica para a próxima vez; as outras seguem.
    /// </summary>
    public static IReadOnlyList<(string Directory, string Reason)> Apply(string widgetsDirectory)
    {
        var errors = new List<(string, string)>();
        var pending = PendingDirectory(widgetsDirectory);
        if (!Directory.Exists(pending))
            return errors;

        // Sobras de trocas anteriores: a pasta antiga só sai quando ninguém mais a usa.
        foreach (var old in Directory.GetDirectories(pending, "*" + OldSuffix))
            TryDelete(old);

        foreach (var marker in Directory.GetFiles(pending, "*" + RemoveSuffix))
        {
            var id = Path.GetFileName(marker)[..^RemoveSuffix.Length];
            var target = Path.Combine(widgetsDirectory, id);
            try
            {
                if (Directory.Exists(target))
                    MoveAside(target, pending, id);
                File.Delete(marker);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add((target, $"remoção pendente não aplicada: {ex.Message}"));
            }
        }

        foreach (var staged in Directory.GetDirectories(pending))
        {
            var id = Path.GetFileName(staged);
            if (id.EndsWith(OldSuffix, StringComparison.OrdinalIgnoreCase))
                continue;
            var target = Path.Combine(widgetsDirectory, id);
            try
            {
                if (Directory.Exists(target))
                    MoveAside(target, pending, id);
                Directory.Move(staged, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add((target, $"atualização pendente não aplicada: {ex.Message}"));
            }
        }

        if (Directory.GetFileSystemEntries(pending).Length == 0)
            TryDelete(pending);
        return errors;
    }

    // Renomear falha se alguma DLL da pasta estiver carregada, e aí nada muda. Depois de renomeada, a
    // pasta antiga sai da frente e é apagada assim que der.
    private static void MoveAside(string target, string pending, string id)
    {
        var old = Path.Combine(pending, $"{id}.{Guid.NewGuid():N}{OldSuffix}");
        Directory.Move(target, old);
        TryDelete(old);
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
