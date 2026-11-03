using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OpenWigi.Ipc;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.System;
using Windows.System.UserProfile;

namespace OpenWigi.Manager.Services;

/// <summary>
/// Nome e foto da conta do Windows deste usuário, pela API do Windows (Windows.System.User), para a lista de
/// sessões da tela do serviço. O serviço não consegue ler as fotos sozinho: elas ficam numa pasta que só o
/// dono e o SYSTEM leem. Sem foto (o Windows não deu, ou a conta não tem), a lista mostra as iniciais.
/// O resultado vai para o manager.log, para dar para entender uma foto que não aparece.
/// </summary>
public static class AccountProfile
{
    /// <summary>Nunca falha: sem resposta do Windows em poucos segundos, vai sem foto e com o nome completo da conta local.</summary>
    public static async Task<UserProfile> LoadAsync(ILogger logger)
    {
        string name = string.Empty;
        byte[] picture = [];
        try
        {
            (name, picture) = await ReadAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Perfil da conta: Windows.System.User falhou ({ex.GetType().Name}: {ex.Message}).");
            logger.LogDebug("Sem nome e foto da conta do Windows: {Message}", ex.Message);
        }

        if (name.Length == 0)
            name = LocalFullName();
        StartupLog.Write($"Perfil da conta: nome \"{name}\", foto {(picture.Length > 0 ? "sim" : "não")}.");
        return new UserProfile(name, picture);
    }

    private static async Task<(string Name, byte[] Picture)> ReadAsync()
    {
        // Num app de desktop a lista traz só o usuário desta sessão (quando traz: sem identidade de pacote,
        // algumas versões do Windows devolvem a lista vazia). Aí a foto vem da API antiga, UserInformation.
        var users = await User.FindAllAsync(UserType.LocalUser, UserAuthenticationStatus.LocallyAuthenticated);
        if (users.FirstOrDefault() is { } user)
        {
            var name = await NameAsync(user);
            var picture = await PictureAsync(user);
            if (picture.Length > 0)
                return (name, picture);
            return (name, await LegacyPictureAsync());
        }

        StartupLog.Write("Perfil da conta: Windows.System.User não listou nenhum usuário; tentando UserInformation.");
        return (string.Empty, await LegacyPictureAsync());
    }

    /// <summary>A foto pela UserInformation (Windows 8+), marcada como obsoleta em favor da User, mas pública e documentada.</summary>
    private static async Task<byte[]> LegacyPictureAsync()
    {
        try
        {
#pragma warning disable CS0618
            var file = UserInformation.GetAccountPicture(AccountPictureKind.LargeImage)
                ?? UserInformation.GetAccountPicture(AccountPictureKind.SmallImage);
#pragma warning restore CS0618
            if (file is null)
            {
                StartupLog.Write("Perfil da conta: UserInformation não deu foto.");
                return [];
            }
            using var stream = await file.OpenReadAsync();
            return await DecodeAsync(stream);
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Perfil da conta: UserInformation falhou ({ex.GetType().Name}: {ex.Message}).");
            return [];
        }
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
    private static async Task<byte[]> PictureAsync(User user)
    {
        var reference = await user.GetPictureAsync(UserPictureSize.Size208x208);
        if (reference is null)
        {
            StartupLog.Write("Perfil da conta: Windows.System.User não deu foto.");
            return [];
        }
        using var stream = await reference.OpenReadAsync();
        return await DecodeAsync(stream);
    }

    private static async Task<byte[]> DecodeAsync(IRandomAccessStream stream)
    {
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

    /// <summary>"Nome completo" da conta local (NetUserGetInfo), que numa conta Microsoft é o nome dela. Vazio se não houver.</summary>
    private static string LocalFullName()
    {
        if (NetUserGetInfo(null, Environment.UserName, 10, out var buffer) != 0 || buffer == 0)
            return string.Empty;
        try
        {
            return Marshal.PtrToStructure<USER_INFO_10>(buffer).FullName?.Trim() ?? string.Empty;
        }
        finally
        {
            NetApiBufferFree(buffer);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct USER_INFO_10
    {
        public string Name;
        public string Comment;
        public string UserComment;
        public string FullName;
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetUserGetInfo(string? server, string userName, int level, out nint buffer);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(nint buffer);
}
