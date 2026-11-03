using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenWigi.Ipc;
using Windows.Graphics.Imaging;
using Windows.System;

namespace OpenWigi.Manager.Services;

/// <summary>
/// Nome e foto da conta do Windows deste usuário, pela API do Windows (Windows.System.User), para a lista de
/// sessões da tela do serviço. O serviço não consegue ler as fotos sozinho: elas ficam numa pasta que só o
/// dono e o SYSTEM leem. Sem foto (o Windows não deu, ou a conta não tem), a lista mostra as iniciais.
/// </summary>
public static class AccountProfile
{
    /// <summary>Nunca falha: sem resposta do Windows em poucos segundos, vai sem nome e sem foto (o serviço usa o login).</summary>
    public static async Task<UserProfile> LoadAsync(ILogger logger)
    {
        try
        {
            return await ReadAsync(logger).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            logger.LogDebug("Sem nome e foto da conta do Windows: {Message}", ex.Message);
            return new UserProfile(string.Empty, []);
        }
    }

    private static async Task<UserProfile> ReadAsync(ILogger logger)
    {
        // Num app de desktop a lista traz só o usuário desta sessão.
        var users = await User.FindAllAsync(UserType.LocalUser, UserAuthenticationStatus.LocallyAuthenticated);
        if (users.FirstOrDefault() is not { } user)
            return new UserProfile(string.Empty, []);
        return new UserProfile(await NameAsync(user), await PictureAsync(user, logger));
    }

    private static async Task<string> NameAsync(User user)
    {
        if (await user.GetPropertyAsync(KnownUserProperties.DisplayName) is string { Length: > 0 } display)
            return display;
        var first = await user.GetPropertyAsync(KnownUserProperties.FirstName) as string;
        var last = await user.GetPropertyAsync(KnownUserProperties.LastName) as string;
        return $"{first} {last}".Trim();
    }

    /// <summary>A foto recortada no quadrado do centro e reduzida para <see cref="UserProfile.PictureSize"/>, em BGRA.</summary>
    private static async Task<byte[]> PictureAsync(User user, ILogger logger)
    {
        try
        {
            var reference = await user.GetPictureAsync(UserPictureSize.Size208x208);
            if (reference is null)
                return [];
            using var stream = await reference.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);

            const int size = UserProfile.PictureSize;
            var scale = (double)size / Math.Min(decoder.PixelWidth, decoder.PixelHeight);
            var width = (uint)Math.Max(size, Math.Round(decoder.PixelWidth * scale));
            var height = (uint)Math.Max(size, Math.Round(decoder.PixelHeight * scale));
            var transform = new BitmapTransform
            {
                ScaledWidth = width,
                ScaledHeight = height,
                InterpolationMode = BitmapInterpolationMode.Fant,
                Bounds = new BitmapBounds { X = (width - size) / 2, Y = (height - size) / 2, Width = size, Height = size },
            };
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, transform,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            var pixels = data.DetachPixelData();
            return pixels.Length == UserProfile.PictureBytes ? pixels : [];
        }
        catch (Exception ex)
        {
            logger.LogDebug("Sem a foto da conta do Windows: {Message}", ex.Message);
            return [];
        }
    }
}
