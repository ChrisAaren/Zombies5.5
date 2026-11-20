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
        public static readonly Color Sneaker = Hex("#D9D4C7");    // light canvas sneakers on the courier

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

        // Combat (survival mode)
        public static readonly Color GunMetal = Hex("#3A3D44");        // gun barrels, receivers
        public static readonly Color GunPolymer = Hex("#26282C");      // matte gun furniture
        public static readonly Color GunGrip = Hex("#1F1F22");         // textured grips
        public static readonly Color SightGlow = Hex("#FF5A3C");       // small emissive sight dot
        public static readonly Color HazardYellow = Hex("#E8B923");    // hazard stripe band on barrels
        public static readonly Color BarrelRed = Hex("#B23A2E");       // explosive barrel body paint
        public static readonly Color WreckChar = Hex("#1F1D1C");       // burnt car wreck
        public static readonly Color HoodGrey = Hex("#4B4F58");        // enemy hoodie hood
        public static readonly Color MuzzleFlash = Hex("#FFD27A");     // muzzle flash and its light
        public static readonly Color TracerWarm = Hex("#FFE2A0");      // bullet tracers
        public static readonly Color SparkYellow = Hex("#FFD36B");     // sparks and fireball core
        public static readonly Color FireOrange = Hex("#FF7A2B");      // fireballs, fire fuse, blast light
        public static readonly Color SmokeGrey = Hex("#6B6A6C");       // smoke puffs
        public static readonly Color SmokeDark = Hex("#2E2D2F");       // dark smoke from burning cars

        // Shared
        public static readonly Color Shadow = Hex("#2B2D42");
        public static readonly Color Accent = Hex("#FFC53D");
        public static readonly Color Danger = Hex("#E63946");

        // Item drops: the glow color of each pickup (matches its model's PickupGlow slot) and the painted body.
        public static readonly Color PickupAmber = Hex("#FFA010");     // Ammo Cache
        public static readonly Color PickupCyan = Hex("#20C8FF");      // Shockwave
        public static readonly Color PickupRed = Hex("#FF2A10");       // Deadeye
        public static readonly Color PickupLime = Hex("#8CFF30");      // Payday
        public static readonly Color PickupBody = Hex("#4B5040");      // worn olive-drab paint
        public static readonly Color PickupMetal = Hex("#3A3D44");
        public static readonly Color PickupPaint = Hex("#B58A1E");     // Ammo Cache stencil paint
        public static readonly Color UiLight = Hex("#FFF4E0");
        public static readonly Color UiDark = Hex("#1E2036");

        // Enemies (survival mode)
        public static readonly Color EnemyHoodie = Hex("#3A3745");   // dark violet-grey hoodie and hood on the hostile NPCs
        public static readonly Color EnemyPants = Hex("#23262D");    // near-black trousers for the hostile NPCs
        public static readonly Color EnemySkin = Hex("#A99C93");     // ashen, desaturated skin so enemies read as unwell
        public static readonly Color EnemyEyeRed = Hex("#FF2A1F");   // emissive red eyes, the enemies' signature read at distance

        // Crosshair options (settings menu)
        public static readonly Color CrosshairGreen = Hex("#5CE08A");
        public static readonly Color CrosshairCyan = Hex("#4FD6E8");
        public static readonly Color CrosshairMagenta = Hex("#E85FD0");

        // Districts (barriers and gates)
        public static readonly Color FenceGalv = Hex("#9BA1A6");      // galvanized posts and chain link

        // Gritty urban pass: weathered walls (multiplied with near-neutral worn textures), hazy warm-gray air.
        public static readonly Color BrickSoot = Hex("#A5604A");       // dull sooty red brick
        public static readonly Color BrickBrown = Hex("#8E6A55");      // brown brick
        public static readonly Color PlasterGrey = Hex("#B3ADA0");     // dirty grey render
        public static readonly Color PlasterOcher = Hex("#C2AE82");    // faded ochre render
        public static readonly Color PlasterBlue = Hex("#8E9AA6");     // faded blue-grey render
        public static readonly Color PlasterSage = Hex("#97A394");     // grimy sage render
        public static readonly Color AsphaltWorn = Hex("#B2ACA4");     // tint for the worn asphalt texture
        public static readonly Color SidewalkWorn = Hex("#C4BDAF");
        public static readonly Color CurbWorn = Hex("#A8A194");
        public static readonly Color HazeFog = Hex("#9E968A");         // warm-gray distance haze
        public static readonly Color SkyZenithGritty = Hex("#4F6E9A"); // muted deep blue overhead
        public static readonly Color SkyHorizonGritty = Hex("#C9B9A2"); // warm-gray, dusty horizon
        public static readonly Color SunHaze = Hex("#FFC98F");         // low warm sun
        public static readonly Color PoleWood = Hex("#6B5A48");
        public static readonly Color DumpsterGreen = Hex("#4E7B62");
        public static readonly Color DumpsterBlue = Hex("#41699A");
        public static readonly Color NewsBoxBlue = Hex("#2E4F8A");
        public static readonly Color NewsBoxRed = Hex("#9E3B34");
        public static readonly Color TrashBagBlack = Hex("#1B1D20");
        public static readonly Color SignCream = Hex("#E8DBB0");
        public static readonly Color SignRed = Hex("#8A2F28");
        public static readonly Color SignGreen = Hex("#2F5B47");
        public static readonly Color SignNavy = Hex("#26395C");
        public static readonly Color SignBrown = Hex("#5A3F2E");
        public static readonly Color SignTeal = Hex("#2E6B6F");
        public static readonly Color GrimeTint = Hex("#3A322B");
        public static readonly Color PaintWorn = Hex("#E6E0D2");

        public static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}
