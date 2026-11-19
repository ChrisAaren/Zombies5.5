using UnityEngine;

namespace Zombies55
{
    // The art bible's palette. Every color in the game reads from here.
    //
    // Style:   Stylized toy-city at golden hour: chunky flat-shaded blocks, rounded characters,
    //          everything that scores glows gold.
    // Mood:    Low late-afternoon sun raking across the avenues; teal-blue sky bounce in the shadows.
    public static class Palette
    {
        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        // Ground
        public static readonly Color Asphalt = Hex("#3A3D4A");
        public static readonly Color RoadLine = Hex("#F2E6C9");
        public static readonly Color Sidewalk = Hex("#D8C9AE");
        public static readonly Color Curb = Hex("#B3A58C");
        public static readonly Color Plaza = Hex("#C2B091");

        // Buildings
        public static readonly Color Terracotta = Hex("#E07A5F");
        public static readonly Color Sage = Hex("#81B29A");
        public static readonly Color Sand = Hex("#F2CC8F");
        public static readonly Color DustyBlue = Hex("#6C8EBF");
        public static readonly Color Window = Hex("#2F3E5C");
        public static readonly Color Trim = Hex("#F4EDE1");

        // Character
        public static readonly Color CharacterPrimary = Hex("#2EC4B6");
        public static readonly Color CharacterSecondary = Hex("#FFE3C8");
        public static readonly Color Pants = Hex("#33415C");

        // Nature and props
        public static readonly Color Leaf = Hex("#5E9C6E");
        public static readonly Color Trunk = Hex("#7A5236");

        // Light and sky
        public static readonly Color SkyTop = Hex("#5E9BD6");
        public static readonly Color SkyHorizon = Hex("#FFD6A5");
        public static readonly Color GroundAmbient = Hex("#5A4636");
        public static readonly Color SunKey = Hex("#FFE2B8");

        // Shared
        public static readonly Color Shadow = Hex("#2B2D42");
        public static readonly Color Accent = Hex("#FFC53D");
        public static readonly Color Danger = Hex("#E63946");
        public static readonly Color UiLight = Hex("#FFF4E0");
        public static readonly Color UiDark = Hex("#1E2036");

        public static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}
