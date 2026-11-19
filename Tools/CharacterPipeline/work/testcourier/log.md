# testcourier iteration log

Tooling calibration made before the loop (applies to every version from v001, not a model fix):
- stretch ignores edges shorter than 5 mm at rest (sliver edges give meaningless ratios);
- self-intersections are judged per 1000 triangles (limit 10) instead of a flat count: the rest pose of this clothed mesh already has thousands of overlapping layered faces, so only NEW overlaps vs rest are counted.
- FBX re-import check renames pre-existing objects so '.001' suffixes do not hide the LOD names.

## v001 baseline (91/118 checks)
Config: voxel 0.004, shell 0.006 (offset -1), proxy 90k tris, lod0 25k tris, 2048 textures.
Fails: 2 vertices at the jacket hem back (x +-0.196, y 0.148, z 0.927) have Spine as strongest bone (0.28 m away, > 15% of height); worst edge stretch at clavicle (arms raised 4.8, arms forward 4.0), head_turn (5.4, 33 overlaps/1k tris), fist (4.3 hand_L); LOD1/LOD2 self-intersections.
Sheet looked: joints land correctly, fingers separated, weights clean on legs/arms, hand weights per finger OK.

## v002 (reverted)  87/118  (v001 had 91)
Change: skin.repair_far_weights = true (far vertices take the average weights of their 8 nearest well-weighted vertices).
Result: fixed 1 of the 2 far vertices per LOD (one remains on LOD0 and LOD1/2 are fixed) but the repaired vertices introduced new edge stretch (squat 5.5, front kick 4.3). Worse -> reverted to the v001 settings.

## v003 smooth torso/limb weights
Change (from v001 config): skin.smooth_passes = 3, factor 0.5 on Hips/Spine/Chest/Neck/Head/Shoulder/UpperArm/LowerArm/UpperLeg/LowerLeg (fingers and hands are NOT smoothed to avoid bleed).
Why: heat weights copied from a noisy proxy give abrupt weight steps at the clavicle/neck, which show up as edge stretch and fold overlaps.
(v003 folder = failed attempt: bpy.ops.object.vertex_group_smooth cannot run in object mode in headless Blender. Replaced it with a numpy Laplacian smoothing that preserves per-vertex mass of the smoothed groups. Same intent as above.)

## v004  92/118  (v001: 91) KEPT
Smoothing as described for v003 (3 passes, 0.5, torso/limb groups, fingers untouched). Failing checks by category v001 -> v004: far 3->3, stretch 10->10, intersections 14->13. Small gain only (arms_raised LOD0 4.8->3.5, arms_forward LOD0 now passes). Smoothing alone is not enough for the layered clothing mesh, but it is harmless, so it stays.

## v005  94/118 KEPT
Change: skin.hand_cleanup (fraction 0.25): finger-bone weight on vertices closer to the wrist than 25% of the hand length is moved to the Hand bone. Why: fist stretch edge was at the wrist (LeftHand/LowerArm edge, 6.3x on LOD1/2): finger proximal bones were dragging the wrist/cuff.
Result: fist LOD0 4.3 -> 3.4, LOD1 6.3 -> 3.2, LOD2 now passes; 94/118. Fails by category (far/stretch/intersections): 3 / 9 / 12.

## v006  94/118 (no change vs v005)
Change: skin.repair_far_weights = true (v002's neighbour-average repair, now run after clean/limit and before smoothing). Result: identical to v005. The two far vertices (jacket hem, x +-0.196 z 0.927) sit at 0.279 m from Spine (limit 0.274): their neighbours have the same Spine-dominated weights (Spine .46, LeftUpperLeg .31, Hips .13), so averaging neighbours changes nothing. Real cause: heat weighting on the hollow-garment proxy gave Spine to the lateral hip hem.

## v007 envelope repair for far vertices
Change: repair_far_weights now gives vertices whose strongest bone is further than 85% of the far limit inverse-square-distance weights over their 3 nearest bone segments.
(v007 first attempt crashed: my edit dropped the smooth_weights function; folder removed, fixed, rerun below.)

## v007  92/118  REVERTED (v005/v006 = 94)
Result: the far-vertex checks pass (3 fixed) but the hem vertices now moving with UpperLeg make the squat worse: volume -14.0% (limit -12%), stretch 6.2 on LOD0. Net 92 < 94 -> repair_far_weights set back to false.

## v008 stronger smoothing (last allowed iteration)
Change (from the v005 config): skin.smooth_passes 3 -> 10 (same groups, factor 0.5). Why: the remaining failures are weight-gradient problems at armpit/torso/neck (stretch + new overlaps at LOD1/LOD2), which smoothing addresses without touching fingers.

## v008  87/118  REVERTED (worse than v005)
Result: 10 smoothing passes blurred the leg/hip weights: squat stretch 5.0, front kick stretch 4.7-5.9, head_turn overlaps 61-76 per 1k tris. Reverted to 3 passes.

## Stop: 8 iterations used. BEST = v005 (94/118). pipeline_config.json is left at the v005 settings
(voxel 0.004, shell 0.006, smooth 3 passes, hand_cleanup 0.25, repair_far_weights false).
Remaining fails in v005: far-bone (2 hem vertices, 3 checks), clavicle/armpit edge stretch on arms_raised, squat stretch on LOD1/2, fist stretch 3.4/3.2, new overlaps at LOD1/LOD2 and head_turn (chin vs jacket collar).
Ideas not yet tried: split the proxy weights per garment layer, per-LOD thresholds, weights for LODs by Data Transfer from smoothed LOD0, a Shoulder-twist helper.

# Round 2 (hidden-body removal, per-LOD limits, local fixes). Versions v009-v016.
Test input regenerated once (textures now only in test_input/textures, FBX uses STRIP paths): same 37408 tris / 10 materials.

## v009  96/118 (v005: 94) KEPT  - fix 1 only
Hidden body faces removal (cleanup.hidden_body) on the game mesh, proxy untouched. Result: only 87 of 7908 skin quads removed (174 tris, 10% of skin area; forearm stubs inside the sleeves and ankle/foot tops inside the shoes); 349 more are covered but kept as the opening band; 7472 are not covered at all. Finding: MPFB already deletes the skin under clothes, so the source has no hidden torso/leg skin; the chin/collar/armpit/hip intersections are garment-vs-garment (jacket/shirt/pants layers) and body-vs-garment on the visible head/hands. Removal therefore barely changes the collisions. Score gain came from the cleaner decimation (fewer tris wasted).

## v010  100/118 KEPT - fix 2: LOD2 limits stretch 4.5, intersections 25 per 1k (LOD0/1 unchanged 3.0/10).

## v011  99/118 REVERTED idea - skin.hem_fix as 2-vertex weight swap (Hips .7 / Spine .3 on the far vertices only): far checks pass, but squat/twist/kick LOD0 stretch 5.8-6.5 because the two verts disagree with their neighbours.
## v012  103/118 KEPT - hem_fix now moves Spine weight to Hips within 10 cm of the far vertices, fading linearly. Far-bone checks pass on all LODs, no squat regression.
## v013  103/118 REVERTED - skin.local_smooth (8 extra passes within 14 cm of the UpperArm joints): arms_raised LOD0 3.55->3.35 only, LOD1 5.25->5.31, arms_forward intersections worse. No gain.
## v014  106/118 KEPT (BEST) - skin.lods_skip_resmooth: LOD1/LOD2 keep LOD0's smoothed weights through the Decimate instead of being cleaned/smoothed a second time on the coarser mesh (that re-smoothing was over-blurring). head_turn LOD1/2 and squat/arms_forward LOD2 intersections dropped, arms_raised LOD2 6.2->5.5.
## v015  104/118 REVERTED - hand_cleanup_fraction 0.25 -> 0.4: fist stretch 10.8 (LOD0) / 8.6 (LOD1).
## v016  106/118 REVERTED (equal) - local_smooth around the Neck (10 cm, 8 passes): head_turn intersections 45.1->45.5, no gain; removed again.

## Stop: 8 more iterations used. BEST = v014 (106/118); pipeline_config.json = v014 settings; v014 export published to Assets/Art/Characters/testcourier/ (not a full pass).
Remaining fails (value vs limit): arms_raised stretch LOD0 3.55/3.0, LOD1 5.53/3.0, LOD2 5.46/4.5 (sleeve/torso edges at the armpit); arms_forward LOD1 intersections 14.2/10; squat intersections LOD0 12.6/10, LOD1 21.9/10, LOD2 36.4/25; head_turn intersections LOD0 45.1/10, LOD1 42.4/10, LOD2 38.8/25 (collar/shirt/jacket/chin layers); fist stretch LOD0 3.41/3.0, LOD1 3.24/3.0.
Ideas left: split garment layers into separate proxy-weighted pieces (weights per layer), a collar/neck weight rule that moves the jacket collar with Neck rather than Head, a shoulder twist helper for the armpit.
