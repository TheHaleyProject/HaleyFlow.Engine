using System.Globalization;

namespace Haley.Services;

/// <summary>
/// Derives a 4-shade accent palette from a single #RRGGBB hex input.
/// Used by timeline renderers to apply per-caller colour theming without
/// modifying the base CSS template.
/// </summary>
internal readonly struct RendererColors {
    public readonly string Base;    // caller's colour as-is
    public readonly string Dark;    // ~25% darker  — text on light backgrounds
    public readonly string Light;   // ~8%  tint    — element backgrounds
    public readonly string Border;  // ~25% tint    — borders
    public readonly int    R, G, B; // raw channel values for rgba() usage

    private RendererColors(string @base, string dark, string light, string border, int r, int g, int b) {
        Base = @base; Dark = dark; Light = light; Border = border; R = r; G = g; B = b;
    }

    /// <summary>
    /// Parses a #RRGGBB hex string and derives the four shades.
    /// Returns false when the input is null, empty, or not a valid 6-digit hex.
    /// </summary>
    public static bool TryParse(string? hex, out RendererColors colors) {
        colors = default;
        if (string.IsNullOrWhiteSpace(hex)) return false;

        var h = hex.Trim().TrimStart('#');
        if (h.Length != 6) return false;

        if (!int.TryParse(h[0..2], NumberStyles.HexNumber, null, out var r) ||
            !int.TryParse(h[2..4], NumberStyles.HexNumber, null, out var g) ||
            !int.TryParse(h[4..6], NumberStyles.HexNumber, null, out var b))
            return false;

        static int  Cl(double v) => v < 0 ? 0 : v > 255 ? 255 : (int)v;
        static string X(int v)  => v.ToString("x2");

        colors = new RendererColors(
            $"#{X(r)}{X(g)}{X(b)}",
            $"#{X(Cl(r * 0.75))}{X(Cl(g * 0.75))}{X(Cl(b * 0.75))}",
            $"#{X(Cl(r * 0.08 + 255 * 0.92))}{X(Cl(g * 0.08 + 255 * 0.92))}{X(Cl(b * 0.08 + 255 * 0.92))}",
            $"#{X(Cl(r * 0.25 + 255 * 0.75))}{X(Cl(g * 0.25 + 255 * 0.75))}{X(Cl(b * 0.25 + 255 * 0.75))}",
            r, g, b
        );
        return true;
    }
}
