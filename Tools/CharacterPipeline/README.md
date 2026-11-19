# Character pipeline (Blender only)

Static humanoid mesh -> rigged, checked, game-ready FBX (Unity Humanoid bone names, `_LOD0/_LOD1/_LOD2`).
Spec: `SPEC.md`. Everything runs headless with Blender 5.2 and its own Python + numpy.

## One command

```
python Tools/CharacterPipeline/run.py <character> <source.fbx|.blend|.obj> [--config f.json] [--no-publish] [--stage N]
python Tools/CharacterPipeline/run.py testcourier Tools/CharacterPipeline/test_input/testcourier_source.fbx
```
Creates `work/<character>/vNNN/` (never overwrites), runs stages 1-5, prints failed checks. On an all-pass
result it copies `export/*` to `Assets/Art/Characters/<character>/`. `--stage N` re-runs stages N.. in the
latest folder. Per-stage logs: `vNNN/log_stageN.txt`. Env `BLENDER` overrides the Blender path.

## Scripts (each: `blender -b --factory-startup --python X.py -- --out <vNNN> [...]`)

| script | does | reads | writes |
|---|---|---|---|
| `make_test_input.py` (NO `--factory-startup`, needs MPFB) | generates the unrigged A-pose test character, one joined mesh, 10 materials | MPFB assets | `test_input/testcourier_source.fbx` (+`.fbm/`, `textures/`, `.blend`) |
| `cleanup.py --character --source --out` | stage 1: apply transforms, face -Y, feet on z=0, scale; watertight voxel PROXY; decimated, UV-unwrapped GAME mesh with baked albedo + normal | source | `cleanup.blend` (HI, PROXY, `<name>_LOD0`), `cleanup.json`, `textures/*.png` |
| `joints.py --out` | stage 2: crotch/armpit/neck/ankle from slices, arm axis + wrist from perpendicular sections, fingertips from geodesic maxima; standard proportions where ambiguous (`estimated`) | `cleanup.blend` | `joints.json` (pos, confidence, method per joint, hand parameters) |
| `skeleton.py --out` | stage 3: deform-only armature via `Tools/Blender/bl_skeleton.build_armature_from_joints` (same names/parents/rolls as the project skeleton), left/right mirrored | `joints.json` | `skeleton.blend`, `skeleton.json` |
| `skin.py --character --out` | stage 4: auto weights on PROXY, Data Transfer to the game mesh, hand cleanup, smoothing, 4 influences, normalize, LOD1/LOD2 by Decimate, FBX export | `skeleton.blend` | `skin.blend`, `skin.json`, `export/<name>.fbx` + textures |
| `check.py --character --out [--prev vNNN-1]` | stage 5: `report.json`, `sheet.png`, `sheet_compare.png` | `skin.blend` + jsons | see below |
| `poses.json` | the 9 stress poses (aim/flex/rot ops, world axes, character faces -Y, left = +X) | | |
| `pl_common.py`, `pl_render.py` | slicing, geodesic tips, numpy tiles/labels (no Pillow) | | |

Unity scale: FBX export uses `FBX_SCALE_ALL`, `apply_unit_scale`, axes -Z forward / Y up, `bake_space_transform`,
`add_leaf_bones=False`, `use_armature_deform_only=True`, transforms applied (armature at origin, scale 1), meters.

## `pipeline_config.json` (copied to `vNNN/config_used.json`; change config, not code)
- `tier` + `tiers.*`: `lod0_tris`, `tris_min/max` (budget check), `texture_size`. `lod_ratios` [1, .5, .2], `lod_ratio_tolerance`.
- `cleanup`: `target_height_m`, `height_range_m` (rescale only outside it), `auto_face`, `source_yaw_deg`,
  `proxy_voxel_size` (must be smaller than the finger gaps), `proxy_shell_thickness`/`proxy_shell_offset` (open garments are thickened before voxel remesh),
  `proxy_max_tris`, `bake_*`, `uv_island_margin`.
- `joints`: `slice_step_m`, `min_confidence`, `snap_limb_joints_to_narrowest`, `hand_slice_fractions`.
- `skin`: `max_influences`, `clean_below`, `smooth_passes/factor/groups`, `hand_cleanup(_fraction)`, `repair_far_weights`.
- `check`: `far_bone_fraction_of_height`, `weight_sum_tolerance`, `mirror_tolerance_m`, `max_volume_loss_pct/gain_pct`,
  `max_edge_stretch_ratio`, `min_edge_len_m`, `max_self_intersections_per_1k_tris`, `max_hand_leak_verts`,
  `max_finger_cross_bleed_ratio`, `max_new_boundary_loops`, `tile_px`.

## Reading `report.json`
`overall_pass`, `summary` (`failed_ids`, `worst_areas` = poses/regions over a limit, `score` = passed/total),
`checks[]` (`id`, `value`, `threshold`, `op`, `pass`, `detail`), `stress.<pose>.LOD0|1|2`
(`volume_change_pct`, `new_self_intersections` [BVH overlaps, neighbouring faces ignored, not present at rest],
`worst_edge_stretch` + `worst_stretch_region` + `worst_edge_rest_pos` + `worst_edge_bones`, per-region stretch/compress/area),
`numbers` (topology, fingers source vs proxy, far vertices, FBX re-import facts).
Check groups: `mesh.*` (budget, holes, watertight proxy, textures), `proxy.fingers_*`, `weights.lodN.*` + hand leak,
`skeleton.*`, `stress.*`, `fbx.*` (FBX re-imported into a clean scene: bone names, no leaf bones, LOD names, height).

## Reading `sheet.png`
Header = verdict. 5-column grid, 512 px tiles, Workbench, fixed orthographic cameras: front/side with joint dots
(green >= .7, yellow low, red below `min_confidence`) and bone lines; mesh colored by strongest bone (white = bone too far);
the 9 stress poses (label `V` volume %, `X` new overlaps, `S` worst stretch ratio; red dots = new self-intersections);
the worst pose on LOD1 and LOD2; close-ups of both hands (top), armpit, shoulder, crotch, knee.
`sheet_compare.png` = previous version (left) vs this one (right), half size.
