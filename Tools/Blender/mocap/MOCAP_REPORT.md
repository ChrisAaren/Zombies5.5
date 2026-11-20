# Mocap research report (Zombies 5.5)

Pipeline: `retarget_mocap.py` (Blender 5.2.1 headless). Raw BVH are in `../references/mocap/` (see SOURCES.md there). Output FBX: `out/Anim_Mocap_<Name>.fbx` (22 clips, 30 fps, same exporter settings as make_animations.py, NOT copied into Assets). Raw numbers: `out/results.json`.

## Sources and licenses
| Source | License | Verdict |
|---|---|---|
| 100STYLE (Mason, Starke, Komura) https://zenodo.org/records/8127870 | CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/), commercial use OK, credit required | used |
| CMU Graphics Lab Mocap Database, BVH conversion by Bruce Hahne (cgspeed; mirror github.com/una-dinosauria/cmu-mocap) | "Use this data! This data is free for use in research and commercial projects worldwide." No resale of the data itself | used |
| Mixamo | Adobe login | skipped |
| CMU re-uploads on Hugging Face, Kaggle, 4TU | conflicting ND/NC labels | skipped |

CMU's own site failed the HTTPS certificate check from this machine, so its wording was confirmed through the converter's README (which quotes CMU) and the cgspeed page ("The files are free to use worldwide for any purpose"). Re-check http://mocap.cs.cmu.edu/info.php in a browser before shipping. An older CMU wording allows inclusion in commercially sold products but forbids reselling the data directly, even converted; ship the clips inside the game, do not publish them as a standalone pack.

**Credits text the game must include:**
- "Motion capture data from the CMU Graphics Lab Motion Capture Database (created with funding from NSF EIA-0196217). The data used in this project was obtained from mocap.cs.cmu.edu."
- "Motion capture data from the 100STYLE Dataset by Ian Mason, Sebastian Starke and Taku Komura, licensed CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/). Retargeted and modified for this game."

## Method
Own BVH parser (data only, nothing executed), forward kinematics, world rotation delta from each rig's T-pose (100STYLE: zero-rotation rest; CMU: T-pose in frame 0), same world delta applied to our bone rest orientations (identical conversion to `make_animations.basis_from`). Hips translation scaled by the leg-length ratio (about 0.97), travel removed (linear drift only, sway and bob kept), vertical offset so the sole points touch z=0, one stride chosen from left-foot touchdowns (best start/end pose and speed match; pose-matching fallback for the 1.3 s CMU run clips), loop closed by cross-blending 0.2 s with the previous (or next) stride, resampled to 30 fps. Speed = original pelvis travel over the stride; tables give the speed after leg scaling (the one to use in game).

## Clips and measured speeds
| Clip | Source | Speed m/s (scaled) | Cadence spm | Frames |
|---|---|---|---|---|
| Walk | CMU 35_01 | 1.26 | 106 | 34 |
| BriskWalk | CMU 07_12 | 2.01 | 139 | 26 |
| Jog | CMU 35_17 | 3.09 | 164 | 22 |
| Run | CMU 09_04 | 3.56 | 164 | 22 |
| Sprint | CMU 16_46 | 4.17 | 180 | 20 |
| SprintAlt | CMU 16_45 | 4.02 | 171 | 21 |
| WalkBack | CMU 136_25 | 0.56 | 72 | 50 |
| CrouchWalk | CMU 136_09 | 0.89 | 97 | 37 |
| StrafeLeft / StrafeRight | 100STYLE Neutral_SW | 0.50 | 95 | 38 |
| StrafeRunLeft | 100STYLE Neutral_SR | 1.23 | 180 | 20 |
| Idle | 100STYLE Neutral_ID | 0 | - | 90 |
| Shamble | 100STYLE Zombie_FW | 0.41 | 75 | 48 |
| ShambleCmu | CMU 104_41 | 0.37 | 64 | 56 |
| ZombieRun | 100STYLE Zombie_FR | 1.20 | 157 | 23 |
| ZombieStrafe, ZombieIdle, OldWalk, LimpLeft, LimpRight, LimpCmu77, LimpCmu91 | see SOURCES.md | 0.2 to 1.4 | | |

Key finding: 100STYLE locomotion is slow (its "run" is a 1.5 m/s jog, strafes 0.5 m/s, zombie 0.4 m/s) and CMU's fastest run found is 4.2 m/s. The procedural clips target Walk 1.6, Jog 3.6, Run 5.0, Sprint 8.5 m/s, so mocap legs cannot reach Run or Sprint speed unchanged.

## Measurements (mocap vs procedural, same code on the baked pose)
Lateral = X axis. Contact = lowest sole point under 2 cm. Slide = planted sole point velocity error when played in place at the clip's own speed, mean, as % of speed.

| Clip | cad spm | speed | step len m | step width m | knee out swing / stance cm | pelvis rot / obl p-p deg | bob cm | arm swing cm | slide % |
|---|---|---|---|---|---|---|---|---|---|
| Walk mocap | 106 | 1.26 | 0.72 | 0.157 | 0.8 / -0.4 | 5.2 / 10.1 | 3.6 | 20.5 | 6.4 |
| Walk procedural | 133 | 1.6 | 0.72 | 0.207 | 3.9 / 2.3 | 8.0 / 8.0 | 5.0 | 44.1 | 0.1 |
| Jog mocap | 164 | 3.09 | 1.13 | 0.131 | 3.6 / 1.8 | 14.0 / 8.7 | 8.6 | 22.1 | 5.7 |
| Jog procedural | 180 | 3.6 | 1.20 | 0.153 | 4.6 / 3.1 | 18.0 / 4.0 | 6.5 | 42.2 | 0.1 |
| Run mocap | 164 | 3.56 | 1.30 | 0.155 | 9.4 / 1.4 | 7.7 / 12.6 | 8.5 | 30.9 | 4.0 |
| Run procedural | 190 | 5.0 | 1.58 | 0.154 | 4.4 / 3.0 | 15.9 / 4.0 | 3.6 | 44.8 | 0.0 |
| Sprint mocap | 180 | 4.17 | 1.39 | 0.145 | 6.0 / 3.2 | 19.3 / 9.3 | 7.9 | 22.3 | 4.3 |
| Sprint procedural | 257 | 8.5 | 1.98 | 0.122 | 6.4 / 4.1 | 24.0 / 2.9 | 7.7 | 55.8 | 0.0 |
| WalkBack mocap | 72 | 0.56 | 0.47 | 0.139 | 4.1 / 1.9 | 5.0 / 5.5 | 1.8 | 5.0 | 9.2 |
| WalkBack procedural | 120 | 1.4 | 0.70 | 0.203 | 3.5 / 2.4 | 8.0 / 6.0 | 6.0 | 20.9 | 0.0 |
| StrafeLeft mocap | 95 | 0.50 | 0.32 | feet cross | 1.1 / 2.7 | 15.0 / 9.9 | 6.8 | 12.5 | 32.6 |
| StrafeLeft procedural | 200 | 1.6 | 0.48 | feet cross | 3.0 / 1.9 | 5.0 / 6.0 | 4.3 | 17.6 | 0.0 |
| Shamble mocap (100STYLE) | 75 | 0.41 | 0.33 | 0.189 | 4.5 / 2.4 | 15.6 / 9.2 | 1.6 | 3.9 | 38.5 |
| Shamble mocap (CMU 104_41) | 64 | 0.37 | 0.34 | 0.434 | 6.2 / 0.9 | 13.4 / 7.4 | 2.1 | 5.6 | 46.2 |
| Shamble procedural | 106 | 1.6 | 0.91 | 0.229 | 5.9 / 4.0 | 14.0 / 10.0 | 5.6 | 14.9 | 0.1 |
| ZombieRun mocap | 157 | 1.2 | 0.46 | 0.190 | 5.0 / 3.1 | 13.4 / 4.0 | 6.7 | 8.9 | 8.1 |
| EnemyJog procedural | 180 | 3.2 | 1.07 | 0.174 | 4.8 / 2.9 | 20.0 / 6.0 | 6.6 | 21.4 | 0.0 |

Reading it:
- Step width: procedural ankles are 0.20 to 0.23 m apart in walks, versus 0.13 to 0.16 m in mocap. The hips are 0.19 m apart, so a 0.20+ m ankle spacing pushes feet and knees outward. This matches the "legs arch out" complaint.
- Knee bowing: procedural knees sit outward on average in every clip (+1.9 to +3.6 cm mean, 3.5 to 6.4 cm swing peak, 2 to 4 cm stance). Mocap means are about 0 (-1.0 to +0.4). The mocap run has a 9.4 cm outward swing peak, which is the heel-kick recovery, not stance bowing.
- Arm swing: procedural 42 to 56 cm versus 20 to 31 cm in real data.
- Cadence at matched speed: procedural walk is 133 spm at 1.6 m/s; real walk is 106 spm at 1.26 m/s.
- Mocap slide of 4 to 9 % on straight clips comes from leg-proportion mismatch and the 30 fps resample; a speed-matched playback rate hides it. Strafe and zombie slides (30 to 46 %) come from the dragging/crossing style at slow pace; they are not clean.
- Stance % is not comparable for runs because mocap includes the flight phase.

## Side-by-side sheets
Top two rows procedural (side, front), red bar, bottom two rows mocap. Files in `Tools/Blender/previews/`: `Mocap_vs_Procedural_<Name>.png` for Walk, Run, Sprint, Jog, BriskWalk, AimWalk (mocap Walk legs vs Anim_AimWalk), StrafeLeft, StrafeRight, Shamble, ShambleCmu, WalkBack, Idle, ZombieRun. Visual check: mocap Walk and Run show narrow tracks, no outward knees, a proper flight phase and heel strike. Mocap StrafeLeft is a slow shuffle (not usable at game speed); procedural strafe shows crossed legs at frames 7 to 11.

## Recommendation
| Clip | Recommendation |
|---|---|
| Walk (1.6 m/s) | Replace with mocap Walk (CMU 35_01, 1.26 m/s). Play at 1.27x or set walk speed to 1.3 m/s. |
| Jog (3.6) | Replace with mocap Jog (CMU 35_17, 3.09 m/s), playback 1.15x. |
| Run (5.0) | Blend: use mocap Run (3.56 m/s) up to about 4.0 m/s at 1.1x; keep procedural only for speeds above that. |
| Sprint (8.5) | Keep procedural, or use mocap Sprint (4.17 m/s) in the 4 to 5.5 m/s band with up to 1.3x playback. No clip near 8.5 m/s exists in these datasets. |
| WalkBack | Keep procedural at game speed, or lower the backpedal speed to about 0.8 m/s and play CMU 136_25 at 1.4x. |
| StrafeLeft/Right | Keep procedural (mocap is a 0.5 m/s side shuffle with crossing feet); narrow its step width to about 0.15 m. |
| AimWalk | Blend: mocap Walk legs, procedural upper body. |
| Shamble (1.6) | Keep procedural unless enemy speed drops to 0.6 to 0.8 m/s (then 100STYLE Zombie_FW at 1.5x to 2x). ShambleCmu has a 0.43 m stance; prefer 100STYLE. |
| EnemyJog | Keep procedural (ZombieRun is 1.2 m/s). |
| Idle | Mocap Idle is usable for variety; blend or keep procedural. |
| Limps, OldWalk, CrouchWalk | New variant options (injured NPC, sneaking); slow, scale playback. |

Procedural fixes the data supports, independent of replacing clips: ankle width 0.20 to about 0.15 m, knee outward means to about 0, arm swing about halved, walk cadence near 106 spm at 1.3 m/s.

## Combining with weapon holds
No dataset has rifle or pistol carry or aim walks. Use the mocap clips for the lower body only. In Unity: keep locomotion in the base layer (same Humanoid bone names), add a weapon layer with an AvatarMask covering spine, chest, neck, head, arms, hands and fingers (no hips or legs) that plays the aim, hold and reload clips, and keep weapon IK for the hands on top. The mocap arm swing then never shows while armed, and only the clip references in the controller change. The mocap pelvis rotates 5 to 19 degrees p-p; the weapon layer's spine pose counter-rotates it, so check the aim direction does not wobble with the stride.

## Blocked / caveats
- CMU homepage certificate error (license text confirmed via mirrors).
- No weapon carry data, no sprint above 4.2 m/s, no clean strafe at game speed.
- Scripts: `retarget_mocap.py` (all clips or by name; flags `--nopreview --noexport --nometrics`), `fetch_100style.py`. Untouched: Assets, bl_skeleton.py, make_animations.py. Files outside the two allowed folders: the `previews/Mocap_vs_Procedural_*.png` sheets, as requested.
