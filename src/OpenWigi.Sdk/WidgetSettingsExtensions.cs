using System.Text.Json;

namespace OpenWigi.Sdk;

/// <summary>Objetos nas configurações, guardados como JSON numa chave só.</summary>
public static class WidgetSettingsExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Lê e desserializa; <paramref name="fallback"/> se a chave não existe ou o JSON não serve mais.</summary>
    public static async ValueTask<T> GetJsonAsync<T>(this IWidgetSettings settings, string key, T fallback,
        CancellationToken cancellationToken = default)
    {
        var text = await settings.GetAsync(key, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(text))
            return fallback;
        try
        {
            return JsonSerializer.Deserialize<T>(text, Json) ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    public static ValueTask SetJsonAsync<T>(this IWidgetSettings settings, string key, T value, bool cloneable = true,
        CancellationToken cancellationToken = default) =>
        settings.SetAsync(key, JsonSerializer.Serialize(value, Json), cloneable, cancellationToken);
}
