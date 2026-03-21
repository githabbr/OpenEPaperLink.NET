using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OEPLLib;

public static class OeplColors
{
    public static readonly Color White = Color.White;
    public static readonly Color Black = Color.Black;
    public static readonly Color Red = Color.ParseHex("#FF0000");
    public static readonly Color Yellow = Color.ParseHex("#FFFF00");

    public static Color Resolve(string? value, OeplAccentColor accentColor = OeplAccentColor.Red)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Black;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "white" or "w" => White,
            "black" or "b" => Black,
            "red" or "r" => Red,
            "yellow" or "y" => Yellow,
            "accent" or "a" => accentColor == OeplAccentColor.Red ? Red : Yellow,
            _ when value.StartsWith('#') => Color.ParseHex(value),
            _ => Color.Parse(value)
        };
    }

    public static Color[] GetDisplayPalette(OeplAccentColor accentColor) =>
        [White, Black, accentColor == OeplAccentColor.Red ? Red : Yellow];

    internal static Rgba32 FindClosestDisplayColor(Rgba32 source, OeplAccentColor accentColor)
    {
        var palette = GetDisplayPalette(accentColor).Select(color => color.ToPixel<Rgba32>()).ToArray();
        var best = palette[0];
        var bestDistance = double.MaxValue;

        foreach (var candidate in palette)
        {
            var dr = source.R - candidate.R;
            var dg = source.G - candidate.G;
            var db = source.B - candidate.B;
            var distance = (dr * dr) + (dg * dg) + (db * db);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }
}

