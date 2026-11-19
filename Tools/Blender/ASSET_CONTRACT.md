# Zombies 5.5: realism asset contract

The single source of truth for every Blender-made asset and how Unity consumes it. The Blender
scripts produce exactly what this file names; Unity's SceneBuilder consumes exactly this.

## Art direction
Grounded, stylized realism: real-world proportions and detail, textured materials and believable
light, but clean shapes and no photoreal clutter. Low-to-mid poly (a car is ~3-6k triangles, a
facade bay ~200-600, a character part ~300-1500). Bevel visible hard edges (2-3 cm, 1-2 segments)
so they catch light. Smooth-shade curved surfaces, auto-smooth / sharp edges on hard ones.

## Units, axes, pivots, export
- Blender units = meters. Unity: 1 unit = 1 m. Y-up in Unity; author Z-up in Blender as usual.
- Export one FBX per asset with `bpy.ops.export_scene.fbx(filepath=..., use_selection=True,
  apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
  apply_unit_scale=True, mesh_smooth_type='FACE', add_leaf_bones=False, object_types={'MESH','EMPTY'})`,
  with all object transforms applied first, so Unity imports it at scale 1, rotation 0.
- "Forward" below means Unity +Z, which is Blender -Y. "Outward" for facades = Unity +Z.
- Pivot = the object origin, placed exactly as each asset row states.
- One mesh object per FBX unless the row says otherwise; mesh name = the file name.
- No colliders, cameras or lights in FBX files. Unity adds colliders itself.

## Files
- Scripts: `Tools/Blender/*.py`, run headless: `blender --background --factory-startup --python <script>`.
- FBX: `Assets/Art/Models/<Category>/<Name>.fbx`; categories: `Architecture`, `Vehicles`, `Props`, `Nature`, `Characters`.
- Textures: `Assets/Art/Textures/<Name>_Albedo.png`, `<Name>_Normal.png` (OpenGL convention, +Y up, linear), optional `<Name>_Mask.png` (R = smoothness multiplier).
- Previews: `Tools/Blender/previews/<Name>.png` (gitignored), Workbench or Eevee renders.

## Materials: slot names (the contract)
Unity maps material slot names to URP materials. Use only these names, exactly. Colors are set in
Unity from the game's Palette, so give Blender materials a plausible viewport color only.

| Slot | Used for | Unity surface |
|---|---|---|
| `Wall` | facade wall surfaces | tinted per building; Brick or Plaster texture |
| `Trim` | cornices, sills, lintels, corner pilasters, ledges | light stone, Plaster texture |
| `Frame` | window and door frames, mullions | painted, slightly glossy |
| `Glass` | window panes, shop windows, car windows | dark, very smooth, reflective |
| `Interior` | dark recess behind glass, car interiors | near-black, matte |
| `Door` | doors | painted wood or metal |
| `Awning` | shop awnings | tinted canvas |
| `Roof` | roof surfaces | gravel/tar, Concrete texture |
| `Metal` | poles, railings, AC units, bins, bench legs | dark painted metal |
| `Concrete` | curbs, planters, fountain stone, bollards | Concrete texture |
| `Wood` | bench slats, crates | Wood texture |
| `Paint` | hydrant, small painted accents | tinted |
| `CarPaint` | car body | tinted per car, glossy |
| `Chrome` | bumpers trim, rims, mirrors | metallic |
| `Rubber` | tires, wipers, seals | dark matte |
| `Headlight` | headlights | emissive warm |
| `Taillight` | taillights | emissive red |
| `LampGlass` | street lamp lens | emissive warm |
| `Bark` | tree trunks and branches | |
| `Leaf` | foliage | |
| `Water` | fountain water | smooth, tinted |
| `Skin` | character skin | tinted per character |
| `Top` | shirt / jacket | tinted per character |
| `Pants` | trousers | tinted per character |
| `Shoe` | shoes | tinted per character |
| `Hair` | hair | tinted per character |
| `Cap` | cap | tinted per character |
| `Bag` | backpack | tinted per character |
| `Eye` | eyes | near-black, glossy |

## UVs and textures
- Surfaces that get a tiling texture (`Wall`, `Trim`, `Roof`, `Concrete`, `Wood`) are UV-mapped in
  **world meters**: 1 UV unit = 1 m, continuous across pieces (cube/box projection in meters).
  Unity sets material tiling to 1 / (texture's real-world size).
- Textures are tileable, 1024 x 1024, and **near-neutral** in hue: they carry value and detail
  (bricks + mortar, concrete grain, cracks) and Unity multiplies them by a Palette color.
  Mean albedo value around 0.7-0.8 so tinting stays predictable.
- Texture set and real-world size: `Brick` 2 m, `Plaster` 2 m, `Concrete` 2 m (sidewalk slab joints
  every 1 m), `Asphalt` 4 m, `Wood` 1 m, `Roof` 4 m. Each has Albedo + Normal; Asphalt and
  Concrete also get a Mask.

## Architecture kit (`Architecture/`)
Facade pieces: pivot at the **bottom center of the outer wall plane**. The piece spans x in
[-1.5, 1.5] (bay width 3 m), y from 0 up, and the wall body runs inward to z = -0.3. Frames, sills
and ledges may protrude outward up to z = +0.2. Adjacent bays tile seamlessly side by side, and
floors stack seamlessly.

| Name | Size (w x h) | Notes |
|---|---|---|
| `Facade_Window` | 3 x 3.2 | upper-floor bay: recessed window ~1.3 x 1.8 (recess 0.15), frame + mullion, stone sill, lintel |
| `Facade_WindowPair` | 3 x 3.2 | two narrower windows ~0.8 x 1.7 |
| `Facade_Blank` | 3 x 3.2 | plain wall with a subtle horizontal band |
| `Facade_Storefront` | 3 x 4.0 | ground floor: large shop window (Glass over Interior), low base, frame |
| `Facade_Entrance` | 3 x 4.0 | ground floor: door with frame and transom glass, step |
| `Corner_Pilaster` | 0.4 x 0.4 x 3.2 | pivot bottom center; sits on a building corner; stacks per floor (ground-floor version `Corner_Pilaster_Ground` is 4.0 tall) |
| `Cornice` | 3 x 0.6 | top-of-building trim segment; pivot bottom center of the outer plane, protrudes to z = +0.35 |
| `Cornice_Corner` | 0.7 x 0.6 x 0.7 | outer corner piece for the cornice |
| `Awning` | 2.8 x 0.9 deep | slanted canvas awning; pivot top-back center (mounts on a facade at y = 3.2) |
| `Roof_ACUnit`, `Roof_WaterTank`, `Roof_Vent` | real sizes | rooftop clutter; pivot bottom center |

Building heights: ground floor 4.0 m, upper floors 3.2 m each.

## Vehicles (`Vehicles/`)
Pivot at the bottom center between the wheels (on the ground), facing forward (+Z). Wheels are
part of the mesh (cars are parked). Rounded, believable bodies: hood, cabin, wheel arches, windows,
lights, mirrors, bumpers.
- `Car_Sedan` ~4.5 x 1.8 x 1.45 m, `Car_Hatchback` ~4.0 x 1.75 x 1.5 m, `Car_Van` ~4.9 x 1.95 x 1.95 m.

## Props and nature (`Props/`, `Nature/`)
Pivot at the bottom center, front facing +Z.
- `StreetLamp` 5 m pole, curved arm, lamp head with `LampGlass`.
- `Bench` 1.8 m wide, wood slats on metal legs.
- `Hydrant` 0.75 m. `TrashCan` 0.95 m. `Bollard` 0.9 m. `Planter` 1.2 x 0.5 x 0.6 m.
- `Crate` 1.1 m cube, wood planks (a stack of two must be climbable; Unity adds box colliders).
- `Fountain` 8 m diameter, stone basin + center pillar + top bowl; separate mesh `Fountain_Water` for the water disc.
- `Tree_A`, `Tree_B` ~6-7 m: tapered trunk with a few branches and clumped, low-poly organic foliage (noise-displaced), two silhouettes.

## Characters (`Characters/`)
Separate meshes per body part so Unity's procedural animation keeps working. Total height 1.8 m
standing. Coordinates below are in the **part's own space** with the pivot at the joint. Proportions
are realistic adult and slightly stylized; no fingers needed (mitten hands are fine), simple
face (nose, brow, ears; eyes as `Eye` slot geometry).

| Name | Pivot (joint) | Extent from the pivot | Slots |
|---|---|---|---|
| `Char_Torso` | hips center | up to the neck base at +0.60, shoulders ~0.44 wide, chest ~0.24 deep, tapered waist | `Top` (plus `Pants` for the hip/belt area) |
| `Char_Head` | neck base | neck + head up to +0.30 | `Skin`, `Eye` |
| `Char_Arm` | shoulder joint | hangs down to the fingertips at -0.66 | `Top` (sleeve), `Skin` (hand) |
| `Char_Leg` | hip joint | down to the sole at -0.95, foot pointing +Z | `Pants`, `Shoe` |
| `Char_HairShort`, `Char_HairLong`, `Char_Cap` | same pivot as `Char_Head` | fits over `Char_Head` | `Hair` or `Cap` |
| `Char_Backpack` | top center of its back face, which sits against the upper back | hangs down and out (-Z) | `Bag` |

Rig placement Unity will use (relative to the character root on the ground, facing +Z):
hips at y = 0.95; legs at x = +/-0.11 from the hips; shoulders at (x = +/-0.21, y = +0.55) above the
hips; neck base at y = +0.60 above the hips; backpack at (0, +0.55, -0.13) above the hips.

## Survival mode additions
New material slots (use exactly): `GunMetal` (dark gunmetal), `GunPolymer` (matte polymer
furniture), `GunGrip` (textured grip), `Sight` (small emissive sight dot or lens), `Hazard`
(warning stripe band), `KioskScreen` (emissive screen), `Hood` (enemy hoodie hood).

### Weapons (`Weapons/`)
Pivot at the **grip, where the right hand closes** (top of the pistol grip, behind the trigger).
The barrel points **forward (+Z)**. Each FBX also contains an **empty named `Muzzle`** at the
barrel tip (its +Z = shot direction). Realistic sizes; clean, modern, original designs (no real
brand or trademarked gun copies); 1.5-4k triangles.

| Name | Length | Notes |
|---|---|---|
| `Gun_Pistol` | ~0.20 m | compact semi-auto sidearm |
| `Gun_SMG` | ~0.50 m | compact SMG, folding stock, long magazine |
| `Gun_Shotgun` | ~0.95 m | pump shotgun, tube magazine, pump grip |
| `Gun_Rifle` | ~0.85 m | assault rifle, rail and optic |
| `Gun_DMR` | ~1.05 m | marksman rifle with a long scope |
| `Gun_LMG` | ~1.0 m | light machine gun, box magazine, bipod folded |

### Props (`Props/`)
- `ExplosiveBarrel` 0.6 m diameter x 0.9 m: steel drum with rolled rims (`Metal`), painted body
  (`Paint`), a `Hazard` band. Pivot bottom center.
- `WeaponKiosk` ~1.3 w x 2.3 h x 0.6 d: a free-standing street arms locker/vending kiosk with a
  glass display front (`Glass`, `Frame`, `Metal`, `Paint`), a small `KioskScreen` price panel above,
  and an **empty named `Display`** inside the glass where Unity floats a copy of the gun on sale.
  Pivot bottom center, front facing +Z.

### Characters
- `Char_Hood` (slot `Hood`): same pivot as `Char_Head`; a hoodie hood that covers the back and top
  of the head and the neck, with an open face, sitting slightly off the head. Enemies wear it.

### Skinned human body (replaces the rigid Char_Torso/Arm/Leg/Head look)
- `Char_Body`: **one smooth skinned mesh** of a whole adult (1.80 m, feet on the ground at y = 0,
  facing +Z), arms hanging down and ~8 deg away from the body, legs straight. Realistic, clean
  anatomy: head with skull, jaw, chin, nose, lips, ears, brow ridge; eyes as separate geometry on
  the `Eye` slot; eyebrows on the `Hair` slot; neck of natural length; trapezius, shoulders,
  chest, waist, hips; arms and legs tapered with elbow and knee shape; hands with a separated thumb
  and a mitten of fingers; shoes with soles. Clothing is painted by slot on the same mesh:
  `Skin` (face, neck, hands), `Top` (shirt/jacket incl. sleeves), `Pants`, `Shoe`, `Eye`, `Hair`.
  6-12k triangles. Smooth shading, no visible seams.
- **Armature** with exactly six bones, named exactly: `Body` (head at the hips, 0, 0.95, 0),
  `LegL` / `LegR` (heads at the hip joints, x = -/+0.11, y = 0.95; parent `Body`),
  `ArmL` / `ArmR` (heads at the shoulder joints, x = -/+0.21, y = 1.50; parent `Body`),
  `Head` (head at the neck base, 0, 1.55, 0; parent `Body`). Character left = -X.
  Weight-paint with smooth falloff across the hip, shoulder and neck joints (and the upper chest,
  so a raised arm pulls the shoulder naturally); every vertex weighted, max 4 influences.
  Unity rebinds the mesh to its own joint transforms by bone name, so bone roll and orientation
  don't matter, only the names and head positions.
- Export with `object_types={'MESH','ARMATURE'}`, `add_leaf_bones=False`, `use_armature_deform_only=True`,
  plus the same axis/scale settings as the rest of the kit.
- Hair and headwear stay separate rigid meshes, re-fitted to the new head, with the same pivot as
  the head joint (neck base at the `Head` bone head): `Char_HairShort`, `Char_HairLong`, `Char_Cap`,
  `Char_Hood`. `Char_Backpack` is re-fitted to the new back, with its pivot at the top center of its back face.
