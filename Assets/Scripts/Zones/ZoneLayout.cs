using UnityEngine;

namespace Zombies55
{
    // The district table and the 5x5 cell grid. Shared by the runtime and the scene builder.
    // Cell (ix, iz) with ix, iz in -2..2 is centered at (ix * 38, iz * 38); its edges lie on the road centerlines.
    public static class ZoneLayout
    {
        public const float CellSize = 38f;
        public const int Half = 2;
        public const int Count = 13;

        public static readonly string[] Names =
        {
            "Fountain Square",   // 0 start
            "Market Row",        // 1 N
            "Tannery Court",     // 2 E
            "Harbor Steps",      // 3 S
            "Chapel Street",     // 4 W
            "Copper Alley",      // 5 NE
            "Lantern Quarter",   // 6 SE
            "Quarry Corner",     // 7 SW
            "Weavers Yard",      // 8 NW
            "Northgate Heights", // 9 north row
            "Southside Docks",   // 10 south row
            "Westend Flats",     // 11 west column
            "Eastend Terrace",   // 12 east column
        };

        public static readonly int[] Costs = { 0, 750, 750, 750, 750, 1000, 1000, 1000, 1000, 2500, 2500, 2000, 2000 };

        public static int DistrictOfCell(int ix, int iz)
        {
            if (ix < -Half || ix > Half || iz < -Half || iz > Half) return -1;
            if (iz == Half) return 9;
            if (iz == -Half) return 10;
            if (ix == -Half) return 11;
            if (ix == Half) return 12;
            if (ix == 0 && iz == 0) return 0;
            if (ix == 0) return iz > 0 ? 1 : 3;
            if (iz == 0) return ix > 0 ? 2 : 4;
            if (ix > 0) return iz > 0 ? 5 : 6;
            return iz > 0 ? 8 : 7;
        }

        public static Vector2 CellCenter(int ix, int iz) => new Vector2(ix * CellSize, iz * CellSize);

        // Cell containing a world position; positions just past the outer road ring clamp to the edge cell.
        public static bool TryCellOf(Vector3 p, out int ix, out int iz)
        {
            const float outer = Half * CellSize + CellSize * 0.5f + 8f;
            ix = iz = 0;
            if (Mathf.Abs(p.x) > outer || Mathf.Abs(p.z) > outer) return false;
            ix = Mathf.Clamp(Mathf.FloorToInt(p.x / CellSize + 0.5f), -Half, Half);
            iz = Mathf.Clamp(Mathf.FloorToInt(p.z / CellSize + 0.5f), -Half, Half);
            return true;
        }

        public static int DistrictAt(Vector3 p) => TryCellOf(p, out int ix, out int iz) ? DistrictOfCell(ix, iz) : -1;
    }
}
