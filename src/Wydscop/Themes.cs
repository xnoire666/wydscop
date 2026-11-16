using System.Windows;
using System.Windows.Media;

namespace Wydscop;

/// <summary>
/// The search bar's two color sets. Every color in SearchWindow.xaml is a {DynamicResource} key set here,
/// so switching themes recolors everything at once.
///
/// Light is a soft gray (not white), Dark is the original warm dark gray.
/// </summary>
internal static class Themes
{
    /// <summary>Colors Windows itself uses for the window: border and whether the acrylic base is dark.</summary>
    public sealed record WindowColors(bool DarkFrame, Color Border);

    public static WindowColors Apply(ResourceDictionary resources, bool dark)
    {
        var palette = dark ? Dark : Light;
        foreach (var (key, color) in palette.Colors)
            resources[key] = Frozen(new SolidColorBrush(color));

        // (The pinned pin is drawn in SearchWindow.xaml as a red push pin, the same in every theme.)

        resources["AcrylicTint"] = Gradient(palette.TintTop, palette.TintBottom);
        resources["SolidBackground"] = Gradient(palette.SolidTop, palette.SolidBottom);
        return new WindowColors(dark, palette.Border);
    }

    private sealed record Palette(
        (string Key, Color Color)[] Colors,
        Color TintTop, Color TintBottom,   // over the acrylic blur
        Color SolidTop, Color SolidBottom, // when there's no acrylic
        Color Border);

    private static Color C(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    /// <summary>
    /// Clean, cool light gray. Mostly opaque so a dark desktop behind it can't turn it muddy; the acrylic
    /// just adds a hint of texture. Cool slate grays for text, and a soft blue highlight for the selected row
    /// that matches the blue accents.
    /// </summary>
    private static readonly Palette Light = new(
        [
            ("Text.Primary", C(0xFF1B1E23)),
            ("Text.Hint", C(0xFF6A717C)),           // search icon, placeholder
            ("Text.Muted", C(0xFF667080)),          // paths, "Recent", unpinned pin
            ("Icon.Subtle", C(0xFF5C6370)),         // ›, ✕
            ("Icon.Hover", C(0xFF1B1E23)),
            ("Accent.Text", C(0xFF1A5FD0)),         // "Show more"
            ("Danger.Text", C(0xFFC42B1C)),         // "Clear all" (deletes)
            ("Danger.HoverBackground", C(0x1AC42B1C)),
            ("Folder.Icon", C(0xFFC28A1C)),
            ("Chip.Background", C(0x14202A3A)),
            ("Divider", C(0x1A202A3A)),
            ("Row.Selected", C(0x262F6FDF)),        // soft blue
            ("Row.Marked", C(0x1F2F6FDF)),          // Ctrl+clicked rows, opened together with Enter
            ("Row.MarkedSelected", C(0x402F6FDF)),
            ("Row.ButtonHover", C(0x1A202A3A)),
            ("Button.HoverBackground", C(0x12202A3A)),
            ("Scroll.Thumb", C(0x38202A3A)),
            ("Scroll.ThumbHover", C(0x55202A3A)),
            ("Scroll.ThumbDrag", C(0x70202A3A)),
        ],
        TintTop: C(0xF0EEF0F3), TintBottom: C(0xF0E6E9ED),
        SolidTop: C(0xFFEEF0F3), SolidBottom: C(0xFFE6E9ED),
        Border: C(0xFFC4C9D0));

    /// <summary>The original warm dark gray.</summary>
    private static readonly Palette Dark = new(
        [
            ("Text.Primary", C(0xFFF2F0EB)),
            ("Text.Hint", C(0xFFA6A39B)),
            ("Text.Muted", C(0xFF8F8C86)),
            ("Icon.Subtle", C(0xFFB5B1AA)),
            ("Icon.Hover", C(0xFFD6D2CB)),
            ("Accent.Text", C(0xFF9CC0FF)),
            ("Danger.Text", C(0xFFFF8A80)),
            ("Danger.HoverBackground", C(0x26FF6B5E)),
            ("Folder.Icon", C(0xFFE8C46A)),
            ("Chip.Background", C(0xFF45413D)),
            ("Divider", C(0xFF3D3A37)),
            ("Row.Selected", C(0xFF45413D)),
            ("Row.Marked", C(0x2E6FA0FF)),
            ("Row.MarkedSelected", C(0x476FA0FF)),
            ("Row.ButtonHover", C(0xFF55514D)),
            ("Button.HoverBackground", C(0xFF3A3734)),
            ("Scroll.Thumb", C(0xFF55514D)),
            ("Scroll.ThumbHover", C(0xFF706B66)),
            ("Scroll.ThumbDrag", C(0xFF86807A)),
        ],
        TintTop: C(0x7A3E3D3C), TintBottom: C(0x7A292523),
        SolidTop: C(0xFF3E3D3C), SolidBottom: C(0xFF292523),
        Border: C(0xFF5C5A58));

    /// <summary>Top-to-bottom over the 56px bar; the results area below keeps the bottom color.</summary>
    private static Brush Gradient(Color top, Color bottom) =>
        Frozen(new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 56)) { MappingMode = BrushMappingMode.Absolute });

    private static T Frozen<T>(T brush) where T : Freezable
    {
        brush.Freeze();
        return brush;
    }
}
