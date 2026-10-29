using MudBlazor;

namespace OpenWigi.Manager.Theming;

/// <summary>
/// Tema do OpenWigiDash Manager: grafite com vermelho "racing" e ciano para dados de sensores.
/// Escuro por padrão, com variante clara. Só fontes do sistema, para funcionar offline no WebView2.
/// </summary>
public static class OpenWigiDashTheme
{
    private static readonly string[] Ui = ["Segoe UI Variable Text", "Segoe UI", "system-ui", "sans-serif"];
    private static readonly string[] Display = ["Segoe UI Variable Display", "Segoe UI", "system-ui", "sans-serif"];

    public static MudTheme Create() => new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = "#E5243B",
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#8B93A7",
            SecondaryContrastText = "#0E0F13",
            Tertiary = "#2EC4C6",
            TertiaryContrastText = "#0E0F13",
            Info = "#4EA8DE",
            Success = "#3DD68C",
            Warning = "#F2B544",
            Error = "#FF6B57",

            Black = "#0E0F13",
            Background = "#121318",
            BackgroundGray = "#17181E",
            Surface = "#1B1D24",
            DrawerBackground = "#15161B",
            DrawerText = "#C9CBD3",
            DrawerIcon = "#8B93A7",
            AppbarBackground = "#15161B",
            AppbarText = "#E8E9ED",

            TextPrimary = "#E8E9ED",
            TextSecondary = "#A0A4B0",
            TextDisabled = "#5C606B",
            ActionDefault = "#A0A4B0",
            ActionDisabled = "#4A4D57",
            ActionDisabledBackground = "#24262E",

            Divider = "#2A2D36",
            DividerLight = "#22242C",
            LinesDefault = "#2A2D36",
            LinesInputs = "#3A3E4A",
            TableLines = "#2A2D36",
            TableStriped = "#1F2128",
            TableHover = "#23252D",
            Skeleton = "#23252D",
            OverlayDark = "rgba(8,9,12,0.7)",
        },

        PaletteLight = new PaletteLight
        {
            Primary = "#C8102E",
            PrimaryContrastText = "#FFFFFF",
            Secondary = "#5B6275",
            Tertiary = "#0E8F91",
            Info = "#1F7CB8",
            Success = "#1E8A57",
            Warning = "#B7791F",
            Error = "#D2452F",

            Background = "#F3F4F6",
            BackgroundGray = "#E9EAEE",
            Surface = "#FFFFFF",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#2A2D36",
            DrawerIcon = "#5B6275",
            AppbarBackground = "#1B1D24", // barra escura também no tema claro
            AppbarText = "#E8E9ED",

            TextPrimary = "#1B1D24",
            TextSecondary = "#5B6275",
            Divider = "#DCDEE4",
            LinesDefault = "#DCDEE4",
            LinesInputs = "#BFC3CC",
        },

        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = Ui },
            H4 = new H4Typography { FontFamily = Display, FontWeight = "700" },
            H5 = new H5Typography { FontFamily = Display, FontWeight = "700" },
            H6 = new H6Typography { FontFamily = Display, FontWeight = "600" },
            Button = new ButtonTypography { FontFamily = Ui, FontWeight = "600", TextTransform = "none" },
            Overline = new OverlineTypography { FontFamily = Ui, FontWeight = "600", LetterSpacing = ".08em" },
        },

        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "6px",
            DrawerWidthLeft = "248px",
            AppbarHeight = "52px",
        },
    };
}
