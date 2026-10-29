using System.Reflection;
using System.Runtime.Loader;
using OpenWigi.Sdk;

namespace OpenWigi.Host.Plugins;

/// <summary>
/// Um contexto por plugin, colecionável, para isolar dependências e permitir descarregar. O contrato
/// e as bibliotecas de UI vêm sempre do host (<see cref="OpenWigiSdk.SharedAssemblies"/>): se cada
/// plugin carregasse a própria cópia, os tipos não bateriam com os do host.
/// </summary>
internal sealed class PluginLoadContext(string entryAssemblyPath)
    : AssemblyLoadContext($"Plugin:{Path.GetFileNameWithoutExtension(entryAssemblyPath)}", isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(entryAssemblyPath);

    internal static bool IsShared(AssemblyName name) =>
        name.Name is { } n && OpenWigiSdk.SharedAssemblies.Contains(n, StringComparer.OrdinalIgnoreCase);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (IsShared(assemblyName))
            return null; // contexto padrão, o do host

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? 0 : LoadUnmanagedDllFromPath(path);
    }
}
