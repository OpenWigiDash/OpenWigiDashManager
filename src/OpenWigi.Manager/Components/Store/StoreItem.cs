using System;
using OpenWigi.Plugins;
using OpenWigi.Sdk;

namespace OpenWigi.Manager.Components.Store;

/// <summary>Um plugin na Loja: o que o feed diz dele (se a busca trouxe) e o que está instalado (se está).</summary>
public sealed record StoreItem(string Id, FeedPackage? Package, InstalledPlugin? Installed, PackageVersion? Update,
    PluginTrust Trust = PluginTrust.Community, string? BlockReason = null)
{
    public string Title => Package?.Title ?? Installed?.PackageId ?? Installed?.Folder ?? Id;

    /// <summary>Quem publicou no nuget.org (owner); sem isso, os autores do pacote.</summary>
    public string? Publisher => Package is null ? null : Package.Owners is { Length: > 0 } owners ? owners : Package.Authors;

    /// <summary>Oficial ou verificado: instala sem o aviso de comunidade. Sem dados do feed (offline), conta como comunidade.</summary>
    public bool Trusted => Trust is PluginTrust.Verified or PluginTrust.Official;

    /// <summary>Copiado à mão para a pasta de widgets: a loja não atualiza.</summary>
    public bool Manual => Installed is { PackageId: null };

    /// <summary>Está no feed mas nenhuma versão serve para este manager.</summary>
    public bool Incompatible => Package is { CompatibleVersion: null };

    public string Requirement =>
        Package?.RequiredSdk is { } sdk
            ? $"Requer o SDK {sdk.Major}.{sdk.Minor} do OpenWigiDash (este manager tem o {OpenWigiSdk.Version.Major}.{OpenWigiSdk.Version.Minor})."
            : "Nenhuma versão serve para este OpenWigiDash.";

    public static string Downloads(long count) => count switch
    {
        >= 1_000_000 => $"{count / 1_000_000.0:0.#} mi downloads",
        >= 1_000 => $"{count / 1_000.0:0.#} mil downloads",
        1 => "1 download",
        _ => $"{count} downloads",
    };
}
