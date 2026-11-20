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
| `make_test_input.py` (NO `--factory-startup`, needs MPFB) | generates the unrigged A-pose test character, one joined mesh, 10 materials | MPFB assets | `test_input/testcourier_source.fbx` (+ `textures/`, `.blend`; textures exist ONCE in `textures/`, the FBX stores bare names with `path_mode='STRIP'` and importers find them by recursive search) |
| `cleanup.py --character --source --out` | stage 1: apply transforms, face -Y, feet on z=0, scale; watertight voxel PROXY (from the full mesh); hidden body faces removed (see below); decimated, UV-unwrapped GAME mesh with baked albedo + normal | source | `cleanup.blend` (HI, PROXY, `<name>_LOD0`), `cleanup.json`, `textures/*.png` |
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

## Hidden body face removal (`cleanup.hidden_body`)
Game mesh and LODs only; the PROXY is built from the full mesh first, so automatic weights still see a watertight body.
Rule: faces whose material name contains an entry of `body_materials` (skin) are BODY, `garment_materials` (Top, Pants, Shoes) are cover; hair/eyes/teeth are never touched.
A body face is removed when 1 + `cone_rays` rays from its centre (normal + `cone_angle_deg` cone) all hit a garment within `max_cover_distance_m`, unless it lies within `opening_band_m` of a garment boundary edge (cuff, collar, hem, ankle). `removed_body` face attribute is kept on HI for the sheet tiles (REMOVED BODY front/side/back, red = removed). `report.json.hidden_body_removal` lists faces/tris/area removed, kept band faces and an estimated per-LOD saving; `mesh.new_boundary_loops` compares against source loops plus the loops the removal leaves. Note: the MPFB test character already lacks skin under its clothes, so only about 87 faces are removed there.

## Per-LOD limits (`check.lod_limits`)
`check.lod_limits["0"|"1"|"2"]` override `max_edge_stretch_ratio` and `max_self_intersections_per_1k_tris` per LOD (missing key = global value). Defaults: LOD0/LOD1 3.0 and 10; LOD2 (lowest detail) 4.5 and 25. Volume and weight limits are global. `report.json.limits_applied_per_lod` lists what was used, and each stress check detail names its LOD limit.

## Other skin options
`skin.hem_fix` (moves one bone's weight to another within a radius of far-bone vertices), `skin.local_smooth` (extra smoothing near a joint), `skin.lods_skip_resmooth` (LODs keep LOD0's smoothed weights).

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

## Stage 6: Unity check (`Assets/Editor/CharacterPipeline/`)

`CharacterImportRule.cs` (AssetPostprocessor, scope `Assets/Art/Characters/**` only): FBX -> Humanoid, avatar from the model,
animation import off, URP Lit material `<name>_Lit.mat` (albedo + normal) remapped onto every FBX material, `*_Normal` textures as
normal maps, `*_Albedo` sRGB. **A-pose handling:** after import it reads the rest pose, then computes an "Enforce T-Pose" equivalent in
code (copy of the hierarchy: upper/lower arm, hand and every finger segment aligned to the +-X axis, upper/lower leg straight down,
foot keeps its rest rotation) and stores it as `HumanDescription.skeleton` with the bone mapping (`SaveAndReimport` only when it differs).
LODs: Unity builds the LODGroup itself from `_LOD0/_LOD1/_LOD2`.

`CharacterUnityCheck.cs` (force reimport, avatar valid/human + all required bones, LODGroup + tris per LOD, height 1.7-1.9, then Anim_Walk /
Anim_AimRifle / Anim_Jog sampled on the character in a `PreviewRenderUtility` scene: NaN bones, feet height, head height, reach, knee/elbow
flips, hands swapped, bone lengths, mesh sanity, 3 screenshots per clip; the game scene is checked to be untouched). Output in the version
folder whose `export/<name>.fbx` equals the published asset (else the latest): `unity_report.json`, `unity_sheet.png`, `unity_<clip>_<n>.png`.

Two entry points, same code:
```
# 1. Editor open on the project (normal path)
unity command script eval -- --code 'return CharacterUnityCheck.Run("testcourier");' --timeout 300000
#    (after editing the scripts: unity command script recompile, then poll unity command script recompile_status)

# 2. Project closed: batch mode (calls EditorApplication.Exit: 0 = all pass, 1 = a check failed, 2 = exception, 3 = bad args). No -quit.
"D:\Unity\6000.6.9f1\Editor\Unity.exe" -batchmode -projectPath "<project root>" -executeMethod CharacterUnityCheck.RunBatch -character testcourier -logFile Tools/CharacterPipeline/work/testcourier/unity_batch.log
```
`run.py <name> <source> --unity` runs stages 1-5 then picks the entry point itself (`unity status` shows a ready Editor on the project -> script eval,
else batch). `run.py <name> --unity-only` runs only the Unity check. Env `UNITY_CLI` / `UNITY_EDITOR` override the paths.
Batch mode cannot open a project that an Editor has open (project lock); close it first with `unity close "<project root>"`.
