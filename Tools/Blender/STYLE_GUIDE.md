# Zombies 5.5: visual target and gap analysis

The user supplied reference screenshots of a gritty, realistic open-world city game and asked for
the game to reach **the same style and level of refinement**: not a copy. The references (not included in this repo) showed:

- street level, character walking away
- street with parked cars and a storefront awning
- tunnel, burning car, character close-up
- night character close-up

**Hard rule:** match the *qualities* (materials, light, density, detail, realism), never the
content. No characters, landmarks (no famous bridges), brands, vehicle designs, logos or signage
from that game or any other. Everything stays original.

## What defines the target look
1. **Light and atmosphere.** Bright, hazy daylight with a warm, slightly sepia, desaturated grade
   and strong contrast. Shadows are deep but soft and warm-gray, never green or blue-black. Heavy
   aerial perspective: distant blocks fade into a bright, milky haze, and the sky near the
   horizon is almost white. Bloom gently halos the sky and bright surfaces. Overall exposure is
   brighter than ours.
2. **Dense, specific architecture.** Varied 3-6 story brick tenements and rowhouses: front stoops
   with steps and iron railings; ground-floor shops with signs, roll-down shutters and awnings;
   deep window reveals with sills and lintels; windows that are *not* uniform black (blinds,
   curtains, reflections, the occasional lit room); belt courses, cornices with brackets, parapets,
   chimneys and water towers. Several brick colors (red, brown, tan, painted). Real massing variety:
   different widths and heights, setbacks, side walls with fewer windows.
3. **Lived-in streets.** Cracked and patched asphalt, a double yellow centerline, curbs with wear,
   stained sidewalk slabs, metal grates and manholes. Cars parked **densely** along the curbs. Street
   lamps on tall poles, utility poles and wires, traffic lights and signs at intersections, street
   name signs (fictional names), parking meters, hydrants, bins, newspaper boxes. Few trees, and the
   ones there are look like real street trees.
4. **Realistic vehicles.** Believable sedans, hatchbacks, vans, pickups and cabs (generic designs):
   panel gaps, chrome trim, glossy clearcoat paint with reflections, tinted glass with visible
   interiors (seats, steering wheel), rims and tires with sidewall detail, light dirt.
5. **Realistic people.** Adult proportions, clothing with folds and seams, believable skin.
6. **Image refinement.** No jaggies or shimmer (temporal or SMAA anti-aliasing), crisp soft
   shadows, ambient occlusion in corners, reflections on glass and paint, high-res textures with
   normal detail, consistent scale.

## Biggest current gaps (by priority)
1. **Toy trees:** faceted low-poly blobs break the realism instantly. Replace them with realistic
   street trees: branching trunks, alpha-cutout leaf cards and a generated leaf texture.
2. **Light and haze:** shadows read blue-green and dark, and the scene reads dimmer than the
   references. Brighten, warm the shadow fill, push the white-ish aerial haze, and warm the grade.
3. **Facade monotony:** windows are uniform dark rectangles; there are no stoops, shutters, quoins,
   brackets, chimneys or balconies; and the window interiors have no variation.
4. **Streets:** too few parked cars; no traffic lights or street signs; white dashed centerlines
   instead of a double yellow line.
5. **Cars:** simple bodies with no interiors and too few models.
6. **Anti-aliasing:** the chain-link fences and fine wires shimmer.

## How to compare
Capture views that match each reference's framing: a third-person shot behind the character down a
street (FOV ~50, camera ~1.7-2.0 m high, ~3 m behind), a sidewalk view along a row of shops with
parked cars, and a character close-up. Put each capture side by side with its reference and list
the differences.

## Real-world photo references (primary realism source)
`Tools/Blender/references/real/real_*.jpg` holds freely licensed Wikimedia Commons street photos;
licenses and authors are in `SOURCES.txt`. They're for internal reference only, gitignored and never
shipped. Real streets are the best realism source, and they keep the game original. Most useful:
- `real_02.jpg`: a tan-brick tenement facade with **zig-zag fire escapes** (platform per floor,
  diagonal stairs between, drop ladder at the bottom), ornate **window hoods/pediments** over
  every window, **AC units** in windows, **window guards** (vertical bars on the lower sash),
  dark green two-over-two sash frames, and soot streaks.
- `real_12.jpg`: a street scene with a **double yellow centerline** and a median, cars **parked bumper
  to bumper** on both curbs, **mast-arm traffic signals** and **cobra-head street lamps** on tall
  poles, striped storefront **awnings** and sign bands, red-painted fire escapes on the corner
  building, street trees with full, soft canopies (not faceted blobs), and a bright sky with a few
  clouds. The sidewalk has slab joints and curb wear.
- `real_09.jpg`: weathered **red brick** with color variation per brick, stone sills and lintels,
  sash windows that **reflect the sky** (glass is not black), **conduit pipes and downspouts** running
  up the walls, bundled overhead **wires**, and black iron fire-escape platforms.
- The others show storefront rows, cast-iron facades, corner buildings and rear courtyards.

Takeaways to apply everywhere: glass reflects (sky and opposite buildings) rather than reading black;
every window has a hood, lintel or sill; facades carry pipes, conduits, AC units and guards; brick
varies brick by brick; fire escapes are the zig-zag type; streets are crowded with parked cars, and
their poles carry lamps, signals and wires.
