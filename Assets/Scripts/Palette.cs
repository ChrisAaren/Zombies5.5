using UnityEngine;

namespace Zombies55
{
    // The art bible's palette. Every color in the game reads from here.
    //
    // Style:   Grounded stylized realism at golden hour: chunky readable blocks in natural materials
    //          (asphalt, concrete, brick, render, limestone), mid-value albedos, everything that scores glows gold.
    // Mood:    Warm low sun with soft contact shadows and a physically based sky; cool blue bounce in the shadows,
    //          filmic grading, bright and readable.
    public static class Palette
    {
        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        // Ground
        public static readonly Color Asphalt = Hex("#5A5C62");
        public static readonly Color RoadLine = Hex("#F2E6C9");
        public static readonly Color Sidewalk = Hex("#C4BCAE");
        public static readonly Color Curb = Hex("#A39C90");
        public static readonly Color Plaza = Hex("#B9AFA0");

        // Buildings
        public static readonly Color Terracotta = Hex("#B8624B");
        public static readonly Color Sage = Hex("#8FA395");
        public static readonly Color Sand = Hex("#DCC9A0");
        public static readonly Color DustyBlue = Hex("#7F95AD");
        public static readonly Color Window = Hex("#3E4D63");
        public static readonly Color Trim = Hex("#E6DFD2");

        // Character
        public static readonly Color CharacterPrimary = Hex("#2A9D8F");
        public static readonly Color CharacterSecondary = Hex("#F2D2B6");
        public static readonly Color Pants = Hex("#3F4B66");

        // Pedestrians (the player's own skin is CharacterSecondary)
        public static readonly Color SkinTan = Hex("#E0AC82");     // warm tan skin tone for NPC faces and hands
        public static readonly Color SkinDeep = Hex("#A9704A");    // deeper golden-brown skin tone for NPCs
        public static readonly Color SkinRich = Hex("#7B4A2E");    // rich dark-brown skin tone for NPCs
        public static readonly Color HairDark = Hex("#3B2A24");    // near-black warm brown NPC hair
        public static readonly Color HairAuburn = Hex("#8C4A2F");  // auburn NPC hair that catches golden light

        // Nature and props
        public static readonly Color Leaf = Hex("#6B8F4E");
        public static readonly Color Trunk = Hex("#6B4F3A");

        // Architecture and street furniture (realism pass)
        public static readonly Color Stone = Hex("#CFC8BA");          // trim, sills, cornices
        public static readonly Color GlassTint = Hex("#1B2633");
        public static readonly Color InteriorDark = Hex("#14161A");
        public static readonly Color FrameLight = Hex("#ECE7DA");
        public static readonly Color FrameDark = Hex("#2F3B3E");
        public static readonly Color DoorGreen = Hex("#2F5D50");
        public static readonly Color DoorNavy = Hex("#2C3E5C");
        public static readonly Color DoorRed = Hex("#7A2E2A");
        public static readonly Color AwningRed = Hex("#B5413A");
        public static readonly Color AwningGreen = Hex("#3F7A58");
        public static readonly Color AwningNavy = Hex("#2F4B7A");
        public static readonly Color AwningCream = Hex("#E2C98A");
        public static readonly Color MetalDark = Hex("#33363D");
        public static readonly Color RubberBlack = Hex("#1C1C1F");
        public static readonly Color ChromeSilver = Hex("#C9CDD2");
        public static readonly Color RoofGravel = Hex("#85817B");
        public static readonly Color ConcreteTint = Hex("#BDB8AE");
        public static readonly Color WoodNatural = Hex("#9A7650");
        public static readonly Color WoodLight = Hex("#C9A877");
        public static readonly Color HydrantRed = Hex("#C23B2E");
        public static readonly Color BollardPaint = Hex("#D9B43B");
        public static readonly Color WaterTint = Hex("#3F8F95");
        public static readonly Color EyeBlack = Hex("#16181D");
        public static readonly Color HeadlightWarm = Hex("#FFF1D0");
        public static readonly Color TaillightRed = Hex("#E03A2F");
        public static readonly Color LeafDark = Hex("#587A40");

        // Car paints
        public static readonly Color CarRed = Hex("#A93232");
        public static readonly Color CarBlue = Hex("#2F5E92");
        public static readonly Color CarWhite = Hex("#E4E1DA");
        public static readonly Color CarSilver = Hex("#A7ABB0");
        public static readonly Color CarGreen = Hex("#3B6B52");
        public static readonly Color CarYellow = Hex("#D9A93A");
        public static readonly Color CarBlack = Hex("#2A2C31");

        // Light and sky
        public static readonly Color SkyTop = Hex("#6A9AD0");
        public static readonly Color SkyHorizon = Hex("#F0C99A");
        public static readonly Color GroundAmbient = Hex("#6B5A4A");
        public static readonly Color SunKey = Hex("#FFD9A6");

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
