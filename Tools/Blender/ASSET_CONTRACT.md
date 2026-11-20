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

## Character v2: humanoid (supersedes the 6-bone `Char_Body`)
- **Skeleton:** `Tools/Blender/bl_skeleton.py` is the only definition: 51 bones with Unity Humanoid
  names (Hips, Spine, Chest, Neck, Head, Left/Right Shoulder, UpperArm, LowerArm, Hand, five
  three-bone fingers, UpperLeg, LowerLeg, Foot, Toes). The rest pose is a **T-pose**. Build the
  armature by calling `bl_skeleton.build_armature()`; never hand-make bones.
- **`Characters/Char_Human.fbx`**: the courier and the base for everyone. One continuous skinned
  mesh (12-25k tris) with clothing modeled in, weighted to that skeleton (max 4 influences, smooth
  joint falloff, edge loops at every joint). Slots: `Skin`, `EyeWhite`, `Iris` (iris + pupil),
  `Brow`, `Hair` (if any is modeled in), `Top`, `Pants`, `Shoe`. Grounded realism: adult
  proportions, readable anatomy, separated fingers, real eyes and eyelids, a shaped nose, lips and
  ears. Textures in `Assets/Art/Textures/Characters/`: `Skin_Albedo/Normal`, `Cloth_*`,
  `Denim_*`, `Leather_*`.
- **Accessories:** separate rigid meshes, re-fitted to Char_Human, with the pivot at the `Head`
  bone head or the `Chest` bone head: `Char2_HairShort`, `Char2_HairLong`, `Char2_Cap`,
  `Char2_Hood`, `Char2_Backpack`.
- **Animations:** `Assets/Art/Animations/Anim_<Name>.fbx`, one clip per file. Armature only (no
  mesh), built from `bl_skeleton`, 30 fps, **in place** (no net root travel), with loops seamless.
  Each clip's speed is the one listed, so Unity can match the blend without foot sliding.
  - Player:
    - **Locomotion:** Idle (loop), Walk (1.6 m/s), Jog (3.6), Sprint (8.0), WalkBack (1.4),
      StrafeLeft (1.6), StrafeRight (1.6).
    - **Jumping:** JumpStart, Fall (loop), Land.
    - **Weapon handling:** AimPistol (loop, two-handed), AimRifle (loop, shouldered),
      HoldRifleLow (loop, ready while moving), ReloadPistol (1.4 s), ReloadRifle (2.3 s).
    - **Death.**
  - Enemy: EnemyIdle (loop, hunched), Shamble (1.6), EnemyJog (3.2), EnemySprint (5.0),
    EnemyAttack (1.1 s, contact at 0.45 s), EnemyHit (0.35 s), EnemyDeath.
  - Fingers: curl into a grip for every aim, hold or reload clip; a relaxed curl elsewhere.

## Districts (buyable map areas)
- `Props/Barrier_FencePanel` (3.0 w x 2.6 h): posts and frame (`Metal`), a chain-link infill on a
  new `Fence` slot (alpha-cutout texture `Textures/Fence_Albedo.png`), and concrete feet
  (`Concrete`).
- `Props/Barrier_Jersey` (2.0 x 0.8 x 0.6): `Concrete` with a `Hazard` stripe.
- `Props/Gate_Terminal` (0.5 x 1.3 x 0.4): a post with an emissive `KioskScreen`.
- `Props/Barrier_Sign` (1.2 x 0.6): a plate (`Paint`, `Hazard`).

## Gritty urban look (environment pass)
Grounded, weathered city realism, our own design: worn and stained materials, street clutter,
overhead utility wires, fire escapes, haze. Original shop names and signage only; no real brands
and no landmarks from any existing game. New texture sets (tileable, same rules as above):
`BrickWorn`, `ConcreteStained`, `AsphaltWorn` (oil stains, patch seams, tire wear),
`PlasterDirty`, `MetalPainted`, `Grime` (overlay/decal), `RoadMarkings`.
New props: `FireEscape_Floor` (one 3.2 m-tall floor segment, mounts on a facade bay), `UtilityPole` +
`Wire` (a sag curve, 1 m along X, to be scaled), `Dumpster`, `TrashBags`, `NewsBox`, `Manhole`, `StormGrate`,
`Storefront_Sign_<1..6>` (original fictional shop names), `AC_WindowUnit`.

### Gritty pass: as built
- Textures (`Tools/Blender/make_textures_gritty.py`): `BrickWorn`, `ConcreteStained`, `AsphaltWorn`, `PlasterDirty`, `MetalPainted`
  (Albedo + Normal + `_Mask` R = smoothness + `_MS` packed R = metallic, A = smoothness for URP Lit's metallic map),
  `Grime` and `RoadMarkings` (Albedo with alpha as an overlay + Normal). Albedo mean ~0.55-0.6, Unity tints it.
- Props (`make_props_gritty.py`): `FireEscape_Floor` (pivot = bottom centre of the facade wall plane, deck at y 0.9, protrudes 1.56 m),
  `UtilityPole` (9.15 m, crossarm along X, wires run along local Z, attach points (-0.9/0/0.9, 8.62) and (0, 9.18)),
  `Wire` (1 m along +X, sag 2% of span, scale UNIFORMLY by the span), `Dumpster`, `TrashBags`, `NewsBox`, `Manhole`, `StormGrate`,
  `AC_WindowUnit` (pivot at the wall plane, sits on the sill at y 0.87), `Storefront_Sign_1..6` (2.6 x 0.6, back at the wall plane,
  mount at y 3.3 over a storefront bay).
- New slots: `SignPanel` (painted metal, tinted per sign), `SignText` (cream lettering), `TrashBag` (black plastic).


## V2 architecture (`Architecture/V2/`, script `Tools/Blender/make_architecture_v2.py`)
Run: `blender --background --factory-startup --python make_architecture_v2.py [-- tex|pieces|preview|street]`
(helpers `bl_v2_textures.py`, `bl_v2_render.py`). Same conventions as v1: pivot = bottom centre of the outer wall plane,
facade bays span x in [-1.5, 1.5], upper floors 3.2 m, ground floor 4.0 m, outward = Unity +Z, 1 UV unit = 1 m. New names only
(`V2_*`), v1 files are untouched, so v1 and v2 can be mixed in one building.

Differences from v1 the builder must know:
- **Wall slab depth.** Upper-floor window bays carry a 0.47 m deep slab (z down to -0.47) because the interior card sits 0.15 m
  behind the glass and the reveal is 0.25 m. Ground-floor pieces keep -0.30. Nothing needs to be placed behind them.
- **Brick UVs.** `Wall` faces use u = x + 1.5, v = y (piece origin at the floor line). The three brick sets are 1.5 m wide x 1.6 m
  tall (6 bricks x 20 courses, course = 0.08 m), so a 3 m bay is exactly 2 tiles and floors of 3.2 / 4.0 m are whole courses: bond lines
  line up between bays and floors. Unity tiling for `BrickRed/Brown/Tan` = (1/1.5, 1/1.6). `StoneTrim` 2 m, `MetalShutter` 1 m.
- **Z-fight rule.** All trim on the wall starts at z = 0 (never overlaps the wall slab); stack pieces without nudging them.
- Left/right pieces (`_L`/`_R`) exist for corners: the building interior is toward +x for `_L` (a building's left corner) and toward -x for `_R`.

### New material slots
| Slot | Used for | Unity surface |
|---|---|---|
| `WindowInterior` | the interior card 0.15 m behind each window's glass: one quad whose UVs select one cell of `WindowInteriors_Albedo.png` | Lit/Unlit, albedo = atlas, **emission = `WindowInteriors_Emission.png`** (only the lamp and TV cells glow; scale it by time of day); no tiling, no tint. Glass in front is reflective with low opacity (alpha ~0.4-0.55, smoothness ~0.95) so the cards show through |
| `Shutter` | roll-down shop shutter curtain | `MetalShutter` texture set, tinted, mildly metallic; mask R = smoothness |
| `SideAd` | faded painted-ad patch on side walls (extra to the two requested) | `SideAds_Albedo.png` with **alpha** (transparent / cutout, no tint); non-tiling, one quad per piece |
Existing slots reused: `Wall`, `Trim`, `Frame`, `Glass`, `Interior`, `Door`, `Awning`, `Roof`, `Metal`, `Concrete`, `Wood`, `SignPanel`.
Frames are meant to read dark green, `Metal` black iron, `SignPanel` a tinted painted board (v1 `Storefront_Sign_*` can be hung on it).

### Textures (`Assets/Art/Textures/`)
| Set | Files | Size / real size | Notes |
|---|---|---|---|
| `BrickRed`, `BrickBrown`, `BrickTan` | `_Albedo`, `_Normal`, `_Mask` (R = smoothness) | 1536 x 1600 / 1.5 x 1.6 m | **Real hue** (mean value 0.39 / 0.31 / 0.56), do not tint strongly; per-brick colour variation, burnt and pale bricks, rough mortar with real depth in the normal, soot streaks, efflorescence |
| `StoneTrim` | `_Albedo`, `_Normal`, `_Mask` | 1024 / 2 m | limestone, near-neutral warm, bedding, pits, rain streaks, grime |
| `MetalShutter` | `_Albedo`, `_Normal`, `_Mask` | 1024 / 1 m | 16 corrugations per metre (horizontal), rust streaks and blotches, dents; tint grey-green |
| `WindowInteriors` | `_Albedo`, `_Emission` | 2048 x 2048 | atlas below |
| `SideAds` | `_Albedo` (alpha) | 2048 x 1536 | 2 x 2 cells of 1024 x 768 |

**Window atlas layout** (4 columns x 3 rows, cell 512 x 680 px, row 0 at the top; UV rect inset 0.002):
| idx | col,row | content | emissive |
|---|---|---|---|
| 0 | 0,0 | blinds half-drawn | |
| 1 | 1,0 | red curtains | |
| 2 | 2,0 | blue curtains | |
| 3 | 3,0 | cream curtains, sofa | |
| 4 | 0,1 | dark room, bookshelf + sofa + floor lamp silhouettes | |
| 5 | 1,1 | window AC unit | |
| 6 | 2,1 | plant behind a sheer | |
| 7 | 3,1 | warm lamp-lit room | yes (lamp + wall glow) |
| 8 | 0,2 | empty dark room | |
| 9 | 1,2 | blinds fully drawn | |
| 10 | 2,2 | TV-lit dark room | yes (cool blue) |
| 11 | 3,2 | green curtains + plant | |
Side-ad cells: 0 STERLING THREAD CO., 1 KESTREL BOOT & SHOE, 2 MERIDIAN TONIC, 3 HOTEL ARDEN (all invented).

### Pieces (size = x width x y height; z = projection from the wall plane, - = into the wall)
Triangle counts are the exported counts.

**Upper-floor bays** (3 x 3.2, pivot bottom centre of the outer plane, slab z -0.47..0). Every window has a 0.25 m deep reveal, a two-over-two
double-hung sash (frame, meeting rail, both sashes with a muntin, one glass pane), a projecting stone sill with corbels (z to +0.13),
and a lintel. Slots `Wall, Trim, Frame, Glass, Interior, WindowInterior` (+ `Metal` for guards).
| Name | Lintel | Guard | Atlas cell | Tris |
|---|---|---|---|---|
| `V2_Window_Flat_BlindsHalf` | flat stone | | 0 | 392 |
| `V2_Window_Seg_CurtainsRed` | segmental stone arch (curved head) + keystone | | 1 | 933 |
| `V2_Window_Key_CurtainsBlue` | flat arch with keystone | | 2 | 408 |
| `V2_Window_Hood_CurtainsCream` | pedimented hood on consoles (z to +0.17) | | 3 | 564 |
| `V2_Window_Flat_DarkFurniture` | flat | bars on lower sash | 4 | 524 |
| `V2_Window_Seg_ACUnit` | segmental | | 5 | 933 |
| `V2_Window_Hood_PlantGuard` | hood | bars | 6 | 696 |
| `V2_Window_Key_LampLit` | keystone | | 7 | 408 |
| `V2_Window_Flat_EmptyDark` | flat | | 8 | 392 |
| `V2_Window_Seg_BlindsClosedGuard` | segmental | bars | 9 | 1065 |
| `V2_Window_Hood_TVRoom` | hood | | 10 | 564 |
| `V2_Window_Key_GreenPlantGuard` | keystone | bars | 11 | 540 |
| `V2_Facade_Juliet` | flat, tall sash window with an iron Juliet balcony (z to +0.40, slab on brackets reaching y -0.10) | | 1 | 1016 |
| `V2_Facade_BeltCourse` | blank wall, stone string + two stepped brick belt-course bands at the top (z to +0.06) | | | 64 |
| `V2_Facade_BrickedUp` | window bricked up 5 cm proud of the reveal, stone sill + lintel stay | | | 184 |
Windows with `Seg` heads reach z -0.47..+0.13; `Hood` to +0.17.

**Corners**: `V2_Corner_Quoin_L` / `_R` (0.74 x 3.2, x -0.22..0.44 or mirrored, z -0.44..0.05, pivot bottom centre of the corner, 8 alternating
stones per floor, 352 tris) and `V2_Corner_Quoin_Ground_L` / `_R` (4.0 tall, 440 tris). Slot `Trim`.

**Side walls** (3 x 3.2, `Wall`, z -0.33..0): `V2_Side_Blank` (12), `V2_Side_Blank_Ground` (3 x 4.0, 12), `V2_Side_Ad_Sterling|Kestrel|Meridian|Arden`
(blank bay + one 2.5 x 1.98 m faded ad quad at z +0.015, slot `SideAd`, 14 tris each). Rotate -90 deg about Y to face -x, etc.

**Wall furniture** (pivot bottom centre of the wall plane x = the pipe axis; stack per floor): `V2_Pipe_Downspout` (3.2 tall, 0.16 x 0.14, wall brackets every 0.8 m, 76 tris),
`V2_Pipe_Downspout_Foot` (4.0, with a kicked-out shoe at the bottom, 112), `V2_Conduit_3_2` (electrical conduit with clamps, a junction box and a short branch run, x -0.62..0.11, 160). Slot `Metal`.

**Ground floor** (3 x 4.0, wall to z -0.30):
| Name | Content | Slots | Tris |
|---|---|---|---|
| `V2_Facade_Stoop` | rowhouse entrance: **5 stone steps** (4 risers + landing at y = 1.0) projecting to z = +1.6, stone cheek walls with sloped tops, newel piers, **iron handrails + balusters on both sides**, recessed door (0.25 m) with panels, glazed upper light and a **transom**, pilasters + entablature + hood, two narrow parlor windows, barred basement windows in the stone base | Trim, Wall, Frame, Door, Interior, Glass, Metal | 1804 |
| `V2_Facade_ShutterShop_Half` | storefront: shop window behind a **roll-down `Shutter` half down** (bottom edge y = 1.55), roller housing, guide rails, **tiled bulkhead** (tile joints), **sign band** y 3.2..3.92 | Trim, Wall, Interior, Frame, Glass, Metal, Shutter, SignPanel | 622 |
| `V2_Facade_ShutterShop_Down` | same, shutter fully down (bottom edge y = 0.68) | same | 622 |
| `V2_Facade_ShopDoorway` | recessed glazed door with transom between two splayed display-window wings, two flush display windows, tiled/stone bulkhead, entry step (z to +0.45), sign band | Trim, Wall, Frame, Glass, Interior, Door, Metal, Concrete, SignPanel | 672 |
| `V2_Facade_ServiceDoor` | steel-framed service door in a 0.25 m reveal (kick plate, louvres, push bar, lock), stone lintel, step (z to +0.55), caged lamp, number plate | Trim, Wall, Metal, Door, Concrete | 436 |
The awning mount height is y = 3.2 as before (below the sign band).

**Roofline** (pivot bottom centre of the outer plane at the top of the wall):
| Name | Size | Notes | Tris |
|---|---|---|---|
| `V2_Cornice_Bracketed` | 3 x 0.86, z -0.30..+0.50 | frieze, **modillion brackets every 0.5 m** (6 per bay, seamless across bays), crown moulding, fascia; slot Trim | 252 |
| `V2_Cornice_Bracketed_Corner_L` / `_R` | 0.8 x 0.87 x 0.8 | mitred outer corner + diagonal corner bracket; pivot at the wall corner | 88 each |
| `V2_Cornice_Corbel` | 3 x 0.56, z -0.34..+0.24 | simple brick corbel table (4 stepped courses), dentil band every 0.25 m, stone cap; Wall + Trim | 244 |
| `V2_Parapet_Coped` | 3 x 0.9, z -0.36..+0.07 | brick parapet with raised panel and **stone coping** overhang; Wall + Trim | 80 |
| `V2_Roof_Chimney_A` | 1.18 x 2.66 x 0.88 | brick shaft, corbelled cap, stone cap, flue pot; pivot bottom centre | 164 |
| `V2_Roof_Chimney_B` | 1.88 x 2.66 x 0.88 | twin-flue version | 236 |
| `V2_Roof_Bulkhead` | 2.56 x 2.64 x 2.75 (z -1.15..+1.6) | roof-access bulkhead: brick box, steel-framed door with louvres (door faces +Z), concrete step, sloped `Roof` top, lamp | 348 |
| `V2_Roof_WaterTower` | 3.0 x 7.75 x 3.0 | **steel stand** (4 legs, ring beams, X-braces on 4 faces and 2 tiers), platform, wooden tank (`Wood`) with 7 steel hoops, conical `Roof` top, riser pipe, valve box and ladder on the +Z side | 1592 |
| `V2_Awning` | 2.8 x 0.58 x 0.9 | v1 mounting (top-back centre at y = 3.2), seam ribs; slot `Awning`, `Metal` arms | 204 |

**Fire escapes (zig-zag, lines up between floors)**: pivot = bottom centre of the facade bay (like v1 `FireEscape_Floor`), protrudes 1.65 m, slot `Metal`.
Each segment is a 2.9 x 1.0 m grated deck (top at y = 0.18, wall brackets down to y = -0.5, rails 0.95 m with 0.15 m balusters, gaps at both stair gates)
plus one full-bay flight in a front lane (z 1.0..1.6) rising 3.2 m to the **next segment's deck**, arriving exactly where the next flight starts.
`V2_FireEscape_ZigA` climbs toward +x, `V2_FireEscape_ZigB` toward -x: **stack A, B, A, B ... one per floor** (each 3.2 m above the last, first at the first upper floor, y = 4.0);
finish the top floor with `V2_FireEscape_Top` (deck with full rails, no flight) and hang `V2_FireEscape_DropLadder` (pivot at the lowest deck level, hangs to y = -3.6, so place it at y = 4.0, x = +/-1.2 at the free stair end).
Tris: ZigA/ZigB 1152 each, Top 768, DropLadder 168. A flight's rail reaches y +4.27 above its segment origin, i.e. into the next floor's space.

### Triangle budget
All 47 pieces together: 21.4k tris (one of each). Window pieces average ~620; a 3-bay, 4-floor building is roughly 10-14k tris incl. stoops, cornice and a fire escape.
The Unity builder should GPU-instance or static-batch by piece name.

## V2 street assets (trees, vehicles, street furniture)
Scripts: `make_trees_v2.py`, `make_vehicles_v2.py`, `make_street_furniture.py` (helpers in `bl_street2.py`; proof scene
`make_street_scene_v2.py` re-imports the FBX). New names only: nothing in the V1 set was overwritten. Same axes/pivot/export rules
as above. Previews: `Tools/Blender/previews/V2s_*.png`.

### New material slots
`LeafCard` (alpha-cutout leaf atlas, two-sided, cutoff ~0.5), `Plate` (license-plate atlas), `SignFace` (street-sign atlas),
`SignalRed`, `SignalAmber`, `SignalGreen` (emissive lamps, each its own slot so Unity can drive them). Other slots as in the table above.
Preview-only Eevee materials live in `bl_street2.py`; the FBX carry plain slot names.

### Trees (`Models/Nature/V2/`), pivot = trunk base on the ground, trunk on the origin
| Name | Height | Crown | Tris | Notes |
|---|---|---|---|---|
| `StreetTree_A` | ~9.2 m | ~9 m | 5.8k | London-plane feel: bare trunk to 3.3 m, 5 spreading limbs, large lobed leaves (cells 1, 3) |
| `StreetTree_B` | ~8.3 m | ~8 m | 7.8k | honey-locust feel: vase shape, airy compound sprigs (cells 2, 3) |
| `StreetTree_C` | ~6.9 m | ~7 m | 6.6k | young maple: slim, oval, dense (cells 0, 3) |
| `TreePitGrate` | 1.2 x 0.035 x 1.2 m | | 1.3k | cast-iron grate, 0.4 m trunk hole, `Metal`; top at y = 0.035 |
- Slots: `Bark`, `LeafCard`. Foliage = folded 4-tri cards with **custom (crown-radial) normals** for soft volume: keep the
  imported normals (Normals: Import), material two-sided (Cull Off), alpha clip 0.5, shadow casting alpha-tested.
- Bark UV: u around the branch, v along it, 0.5 m per texture tile. `BarkV2_Albedo/Normal.png` (512, tileable, near-neutral: tint it).
- `LeafCards_Albedo.png` (2048, RGBA, straight alpha, RGB bled into transparent texels) and `LeafCards_Normal.png`:
  2 x 2 atlas, each cell 1024 px: cell 0 (top-left) maple cluster, 1 (top-right) plane-tree cluster, 2 (bottom-left) honey-locust
  sprig, 3 (bottom-right) small broadleaf filler. Mean opaque albedo ~(0.35, 0.39, 0.14): slightly yellow-green, desaturated.

### Vehicles (`Models/Vehicles/V2/`), pivot bottom center between the wheels, forward +Z, 18-20.4k tris each
`CarV2_Sedan` 1.86 x 1.45 x 4.57, `CarV2_Coupe` 1.88 x 1.35 x 4.47, `CarV2_Hatch` 1.78 x 1.51 x 4.09, `CarV2_Wagon` 1.86 x 1.46 x 4.77,
`CarV2_Van` 1.93 x 1.95 x 4.94, `CarV2_Pickup` 1.98 x 1.70 x 5.26 (open bed), `CarV2_Cab` 1.86 x 1.65 x 4.57 (sedan body + roof sign box),
`CarV2_SUV` 1.94 x 1.74 x 4.67, `CarV2_Minivan` 1.94 x 1.77 x 4.94 (sizes x, y up, z forward).
- Slots: `CarPaint`, `Glass`, `Chrome`, `Rubber`, `Interior`, `Headlight`, `Taillight`, `Metal`, `Paint`, `Plate`, and `LampGlass` (Cab sign).
  `Metal` = steel wheels / brake discs, `Paint` = brake calipers and the Cab sign body (tint freely).
- Greenhouse is open to the cabin: a dark lining shell (`Rubber`, normals inward: needs back-face culling ON), seats, dash and wheel
  (`Interior`) show through `Glass`, so keep Glass transparent/tinted (alpha ~0.3-0.5), not opaque.
- Details (all geometry): panel-gap grooves (hood, trunk/hatch, door outlines, `Rubber`), chrome belt and bumper trim, door handles,
  mirrors, wipers, arch trim, side markers, grille slats, headlight bezel + reflector cup + bulb, taillight lens with reflectors, exhaust,
  recessed plate frames.
- Wheels: 24-sided tires with sidewall ridge and 4-rib tread (`Rubber`), spoked rims (5 or 6 spokes) or steel wheels with hubcaps.
- `PlatesV2_Albedo.png` (1024 x 512): 4 x 4 cells of 256 x 128 px, fictional "ZOMBIES 5.5 / ZB 5.5-nnn". Cell index = row * 4 + col from
  the top-left; the car with index i (Sedan 0, Coupe 1, Hatch 2, ...) uses cell 2i (front) and 2i+1 (rear) mod 16; the Cab uses cell 9
  (yellow) both ends. Plate quads are 0.305 x 0.152 m with UVs already inside their cell.
- Dirt: 2nd UV map **`UVDirt`** (Unity uv2) maps `CarDirt_Mask.png` (512, R = G = B = dirt amount 0..1). u = along the body remapped so
  the rear axle = 0.2 and the front axle = 0.8 (identical for every car, so the arch blobs always line up); v = height / 2 m
  (v = 0 is the ground, so dirt gathers at the sills and fades by ~0.6 m); nose/tail faces sample u = 0.5. Use it in a shader
  to lerp CarPaint toward a grime color/lower smoothness. First UV map is world-meter box projection.

### Street furniture (`Models/Props/V2/`), pivot bottom center on the ground, heads/signs face +Z, arms extend to +X
| Name | Size (x, y up, z) | Slots / notes |
|---|---|---|
| `TrafficLight_Pole` | 5.8 x 6.45 x 0.7 | mast arm (5.6 m) with two 3-lamp heads at x = 2.3 and 4.3, plus one head on the pole at 2.7 m. `Metal`, `SignalRed`/`SignalAmber`/`SignalGreen` |
| `TrafficLight_Post` | 0.46 x 3.88 x 0.8 | 3.7 m post, one 3-lamp head with backplate, push button |
| `StreetSign_Post` | 0.9 x 3.37 x 0.9 | 3.35 m post, two crossed name blades (1 along +X, 1 along +Z), faces = cells 0-3 of the blade atlas; `StreetSign_Post_B` = same with cells 4-7 |
| `StopSign` | 0.75 x 2.58 | octagon, 0.75 m across flats |
| `NoParkingSign` | 0.45 x 2.52 | 0.45 x 0.6 m face |
| `BusStopSign` | 0.45 x 3.12 | blue flag sign + small info plate |
| `ParkingMeter` | 0.2 x 1.63 | `Metal`, `Glass`, `Paint`, `Chrome` |
| `PhoneBooth` | 1.0 x 2.56 x 1.0 | frame, glass, phone unit, `TELEPHONE` header (cell below); generic, unbranded |
| `StreetLamp_V2` | 3.4 x 9.4 | 9 m tapered pole, curved arm to +X 2.5 m, cobra head, `LampGlass` lens underneath |
| `Bollard_V2` | 0.23 x 0.92 | `Metal` + `Paint` reflective band |
| `CurbDrain` | 1.3 x 0.185 x 1.03 | curb throat (`Concrete`, `Interior` opening, back at z = -0.2) + gutter grate toward +Z, 0.9 x 0.45 |
- `StreetSigns_Albedo.png` (2048 x 2048, RGB, `SignFace`). Cells in pixels, origin top-left (Unity v = 1 - y / 2048):
  name blades 1024 x 256 each, 2 columns x 4 rows at (col * 1024, row * 256), index b = row * 2 + col: 0 MERCER AVE, 1 HALSEY ST,
  2 ORCHARD BLVD, 3 GARNET ST, 4 PELL AVE, 5 RUSKIN ST, 6 DALTON RD, 7 FENWICK LN. Lower half: STOP octagon (0,1024)-(512,1536);
  NO PARKING (512,1024) 384 x 512; BUS STOP (1024,1024) 384 x 512; TELEPHONE header (1536,1024) 512 x 128;
  ONE WAY (0,1536) 512 x 160 (unused by a prop yet); PAY HERE (512,1536) 256 x 128 (unused). Sign backs are `Metal`.
- `RoadMarkingsV2_Albedo.png` (2048 x 2048, RGBA straight alpha): 4 x 4 cells of 512 px, each cell = **2 m x 2 m** of road, index
  i = row * 4 + col from the top-left (Unity u0 = col / 4, v0 = 1 - (row + 1) / 4). Lay it as decal quads (+0.01 m) or URP decals:
  0 double yellow centerline (two 0.1 m lines, 0.1 m gap, vertical, tiles along v), 1 same worn, 2 same worn + cracked,
  3 white solid line 0.12 m, 4 white dashed lane line (1.2 m dash, 0.8 m gap), 5 white solid worn, 6 white solid cracked,
  7 yellow single line, 8 stop bar (0.5 m deep, tiles along u), 9 stop bar worn, 10 crosswalk bars (0.5 m bars, 0.5 m gaps, bars run
  along v, tiles along u), 11 crosswalk worn, 12 straight arrow, 13 crack decal (near-black, alpha 0.75), 14 asphalt patch decal,
  15 tire-wear bands decal. Cells 0-12 carry paint color, 13-15 are dark overlays.

## V3 vehicles (generic late-2000s / early-2010s everyday cars)
Script: `make_vehicles_v3.py` (helpers from `bl_street2.py`; `--only=Sedan,Cab`, `--noprev`, `--nolineup`). Output: `Assets/Art/Models/Vehicles/V3/CarV3_*.fbx`.
V2 files are untouched. Same conventions as V2: pivot = ground, bottom centre between the axles; forward = Unity +Z; X right; Y up; FBX export with
`axis_forward -Z / axis_up Y`; same slots; `UVDirt` (uv2) for `CarDirt_Mask.png` (rear axle u = 0.2, front axle u = 0.8, v = height / 2 m); plate quads UV into
`PlatesV2_Albedo.png` (Sedan cells 0/1, Compact 2/3, Hatch 4/5, Minivan 6/7, SUV 8/9, Pickup 10/11, Van 12/13, Cab 9 yellow at both ends).
Bounds are the full mesh including door mirrors (x = width over mirrors, y up, z forward; z min/max are the tail / nose extremes from the pivot).

| Name | Size x, y, z (m) | z min .. max | Tris | Notes |
|---|---|---|---|---|
| `CarV3_Sedan` | 1.95 x 1.45 x 4.80 | -2.44 .. 2.36 | 27.2k | mid-size, wheelbase 2.72, 5-spoke alloys |
| `CarV3_Compact` | 1.88 x 1.42 x 4.50 | -2.27 .. 2.24 | 27.7k | wheelbase 2.60, steel wheels + hubcaps |
| `CarV3_Hatch` | 1.84 x 1.47 x 4.06 | -1.97 .. 2.09 | 26.3k | wheelbase 2.45, steel wheels, tailgate shut line |
| `CarV3_Minivan` | 2.00 x 1.73 x 5.00 | -2.57 .. 2.44 | 29.8k | wheelbase 2.92, 6-spoke alloys, sliding-door seams, hatch |
| `CarV3_SUV` | 1.99 x 1.73 x 4.76 | -2.39 .. 2.38 | 28.2k | wheelbase 2.78, ground clearance ~0.22 m, 6-spoke alloys |
| `CarV3_Pickup` | 2.07 x 1.73 x 5.40 | -2.80 .. 2.60 | 27.2k | open bed (liner, wheel houses, tailgate), chrome bumpers, steel wheels |
| `CarV3_Van` | 2.06 x 1.99 x 5.06 | -2.57 .. 2.49 | 25.6k | boxy delivery van, windowless cargo box, rear doors with windows |
| `CarV3_Cab` | 1.95 x 1.63 x 4.80 | -2.44 .. 2.36 | 27.7k | Sedan body in yellow-ish tint + roof sign (`Paint` body, `LampGlass` lenses), steel wheels |

- Slots (identical on every car, wreck swap by slot name): `CarPaint`, `Glass`, `Chrome`, `Rubber`, `Interior`, `Headlight`, `Taillight`, `Metal`, `Paint`, `Plate`;
  `LampGlass` only on the Cab sign. `Metal` = steel wheels, brake discs, underside, vent slats; `Paint` = brake calipers and the Cab sign body.
  `Interior` is the only material inside the cabin (seats, dash, wheel, door cards, floor, headliner): give it a fabric/plastic grey-beige, not black rubber.
- Glass is a **separate shell** (disjoint mesh island, normals out) sitting in rubber-gasketed openings (`Rubber` frame faces): keep `Glass` transparent / tinted (alpha ~0.25-0.4);
  the interior shows through. `Headlight` clusters carry a clear `Glass` cover over chrome reflector cups and `Headlight` bulbs; taillight lens = `Taillight`, reverse lamp = `Headlight`.
- Body = subdivided quad cage (Subsurf level 2 applied). Wheel arches are boolean-cut with a `Rubber` well liner; wheels (tire with sidewall bulge, rim lip / dish / spokes or hubcap, disc, caliper, lug nuts) sit ~0.03-0.05 m inside the fender line.
- Details (geometry): panel-gap grooves (hood, trunk / tailgate, door outlines), door handles, mirrors on stalks, grille slats, fog lamps, bumper shut lines, wipers, cowl strip, recessed plate frames, tailpipe.
- Back-face culling should be ON for `Rubber` (liner / well faces point toward the cavity). Previews: `previews/V3_Car_<Name>_{front34,rear34,side}.png`, curb lineups `previews/V3_Lineup_{curb,curb_close,across_a,across_b}.png`.

## Pickups (`Models/Pickups/`, script `make_pickups.py`)
Run: `blender --background --factory-startup --python make_pickups.py [-- noprev]`. Power-ups dropped by enemies; Unity hovers and spins them about the pivot.
Pivot = bottom centre of the footprint (on the origin), +Z forward, bevelled edges. One mesh per FBX (name = file name). Sizes are x wide, y up, z deep (m).
| Name | Size | Tris | Slots | Glow parts |
|---|---|---|---|---|
| `Pickup_AmmoCache` | 0.52 x 0.43 x 0.32 | 1042 | `PickupBody`, `PickupMetal`, `PickupGlow`, `PickupDecal`, `Paint` | band around the lower crate |
| `Pickup_Shockwave` | 0.46 x 0.52 x 0.43 | 2034 | `PickupBody`, `PickupMetal`, `PickupGlow`, `PickupDecal` | floating ring + core shaft between the ribs |
| `Pickup_Deadeye` | 0.52 x 0.54 x 0.52 | 2096 | `PickupBody` (brass case), `PickupMetal` (bullet, reticle ring), `PickupGlow`, `PickupDecal` | bullet tip |
| `Pickup_Payday` | 0.51 x 0.41 x 0.37 | 836 | `PickupBody` (cash), `PickupMetal` (gold clip), `PickupGlow`, `PickupDecal` | bank bands on the bricks |
- `PickupGlow` faces are separate faces (never shared with other slots) so Unity can tint and pulse them; viewport colours are plausible only.
- `PickupDecal`: single atlas `Assets/Art/Textures/Pickups_Decals.png` (1024 x 1024 RGBA, straight alpha), 2 columns x 4 rows of 512 x 256 cells,
  cell i = col i % 2, row i // 2 from the top. 0 "CAL. MIXED / RESUPPLY", 1 "LOT 55-C / CACHE-A", 2 "ARC-7 / EMITTER", 3 hazard stripes (opaque),
  4 "DEADEYE / .50 AP" headstamp, 5 reticle ticks (wraps a ring), 6 fictional banknote face, 7 "FIRST CITY / TREASURY 10,000" band label.
  Decal faces float 1.2 mm above the surface with hand UVs: material = alpha-blend or cutout, no tiling, no tint (or tint only lightly). All other faces use world-meter box UVs.
- Previews: `previews/Pickup_<Name>_34.png`, `_far.png` (10 m, camera 1.7 m), `Pickup_Lineup.png`, `Pickup_Lineup_far.png`.
