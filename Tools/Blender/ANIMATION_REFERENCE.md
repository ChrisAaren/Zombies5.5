# Zombies 5.5 animation reference, gap analysis and fix list

Scope: research and analysis only. No game code was changed and Unity was not run. Contact sheets (`Tools/Blender/previews/Anim_*.png`) and the authoring parameters in `make_animations.py` were read. The in-game captures in `%TEMP%\zombies55-look` were not inspected (only `.cs` helper scripts were listed there), so every "in game" statement below comes from reading code and is marked **verify** where it needs a Play Mode check.

Conventions
- `[S#]` is a source in section 6. **(est.)** means my estimate or engineering judgement, not a sourced number.
- 30 fps conversion: one full gait cycle (two steps) takes `120 / cadence_spm` seconds, i.e. `3600 / cadence_spm` frames. A step is half of that.
- "Cycle" = left foot contact to the next left foot contact. `Loco.T` in `make_animations.py` is the cycle length in frames.

---

## 1. Reference data

### 1.1 Gait phases (walking), % of the cycle [S1]

| Phase | % of cycle | Frames of a 30-frame cycle | Notes |
|---|---|---|---|
| Loading response | 0-12 | 0-4 | Double support. Knee 0 to ~20 deg flexion, ankle to ~5 deg plantarflexion, hip ~20-30 deg flexed |
| Mid-stance | 12-31 | 4-9 | Single support. Hip goes to neutral, knee back to ~0-5 deg, ankle to ~5 deg dorsiflexion. Pelvis is highest here |
| Terminal stance | 31-50 | 9-15 | Heel rises, hip extends ~10-15 deg |
| Pre-swing | 50-62 | 15-19 | Double support. Knee to ~40 deg flexion, ankle plantarflexed ~20 deg |
| Initial swing | 62-73 | 19-22 | Knee 40-60 deg |
| Mid-swing | 73-87 | 22-26 | Peak knee ~60 deg early in swing, then extends |
| Terminal swing | 87-100 | 26-30 | Knee extends, hip ~30 deg flexed, ankle neutral, heel strike |

- Stance about 60%, swing about 40%. Each double-support period is about 10% (20% total). A run has no double support, only two flight phases [S1][S6].
- Walking cadence: 100 steps/min is the usual threshold for "brisk" walking (about 3 METs) [S8]. Laboratory normal range is roughly 81-138 steps/min [S8].
- Step length / cadence is approximately constant across speeds (the "walk ratio"). I could not fetch the paper, so the value is **(est.)**: step length is about 0.0065 m per (step/min). That implies about 122 spm and 0.79 m steps at 1.6 m/s.

### 1.2 Running and sprinting

| Quantity | Jog (~3-4 m/s) | Run (~5 m/s) | Sprint (8-10 m/s) | Source |
|---|---|---|---|---|
| Cadence | 169 spm at 2.7 m/s, 178 spm at 3.8 m/s (self-selected 3.2 m/s = 168) | about 185 spm **(est.)**, extrapolating the same ~6-7 spm per m/s slope | 4.1-4.4 Hz at 8 m/s (246-264 spm); 4.6 Hz at ~10 m/s | [S2] [S3] |
| Step length | about 1.0 m at 2.7 m/s, 1.3 m (stride estimate) at 3.8 m/s per [S2] | about 1.6 m **(est.)** (speed / cadence) | 1.84-1.95 m at 8 m/s; 2.15 m at 9.9 m/s | [S2] [S3] |
| Stance (contact) time | about 40% of cycle | about 33-35% **(est.)** | about 22% | [S6] (compedgept sprint page) |
| Peak knee flexion in swing | ~90 deg | ~90-100 deg **(est.)** | 105 deg (range 105-130) | [S6] |
| Knee flexion at mid-stance | ~40 deg | ~40-45 deg **(est.)** | | [S6] |
| Hip | ~60 deg flexion at end of swing, ~15 deg extension at toe-off | | up to ~90 deg flexion | [S6] |
| Elbow | Bent, forearm near perpendicular to upper arm (about 90 deg), "bent arm running" | | 90 deg, driven back, not across the body | [S7] [S6] |
| Arm swing | Amplitude grows with speed. Arms act as mass dampers cancelling trunk yaw; effect is stronger in running | | | [S7] |
| Trunk lean | 5-10 deg **(est.)** | 8-15 deg **(est.)** | 10-20 deg at top speed **(est.)**; 40-50 deg in the first acceleration steps | acceleration value from [S6]-adjacent sprint sources; rest **(est.)** |
| Head | Rotation amplitude is lower than shoulder rotation in all gaits. Head is stabilised | | | [S7] |

- Walk-to-run transition: about 2.0 m/s (range 1.9-2.1), Froude number about 0.5 (`Fr = v^2 / (leg_length * g)`) [S4]. Anything faster than ~2.2 m/s should be a run (with flight phase) or an obviously "power walk".
- Cadence rises only slowly with speed; step length does most of the work. Cadence is a poor knob for speed matching: a 40% cadence change reads as wrong [S2].

### 1.3 Pelvis and centre of mass (walking) [S5]

| Quantity | Value |
|---|---|
| Vertical CoM displacement | about 5 cm peak-to-peak at average walking speed |
| Lateral CoM displacement | about 5 cm peak-to-peak |
| Pelvic rotation (transverse) | +-4 deg (8 deg total): swing-side hip forward |
| Pelvic obliquity (list) | about 5 deg drop on the unsupported (swing) side at mid-stance |
| Later critiques | Pelvic rotation and list contribute little to vertical CoM reduction, but they are visually essential for believability [S5] |

Running vertical CoM oscillation is typically larger than walking, roughly 6-10 cm **(est.)**, and smaller at sprint **(est.)**.

### 1.4 Key poses and frames [S9]

Williams-style 24 fps timing (per full cycle, two steps): 8-12 frames = very fast run, 16 = slow run, 24 = brisk natural walk, 32 = relaxed stroll, 40+ = tired or elderly. Contact at frames 1/13/25, down at about 4/16, passing at about 7/19, up at about 10/22. In 30 fps terms with our cycle lengths:

| Pose | Position in cycle (walk) | In a 30-frame walk | In a 20-frame run |
|---|---|---|---|
| Contact (heel strike) | 0% | f0 | f0 |
| Down (lowest point, weight accepted) | ~12% | f3-4 | f2 |
| Passing (swing foot passes the stance knee, pelvis high) | ~31% | f9 | f4-5 (run: both feet off ground ~f6-7) |
| Up (push-off) | ~43-50% | f13-15 | f8-9 |

Overlap and follow-through (secondary motion lags the pelvis by 2-5 frames: hands, head, hair, gun) is what stops a cycle looking like a rigid puppet [S9][S10].

### 1.5 Industry practice for locomotion in third-person games (general, not game-specific)

| Topic | Practice | Source |
|---|---|---|
| Starts and stops | Lean into the start, lean back on the stop. A character that reaches full speed instantly looks weightless without lean and a push-off | [S10] (Cooper, 12 principles) |
| Pivot / plant-and-turn | Faster movement needs bigger turning circles or longer plant-and-turn clips for 180 turns | [S10] |
| Turning circles | Bank (lean) into turns, and lead with the head so the intent reads early | [S10] |
| Turn in place | 90 and 180 deg left/right clips, used when stationary | [S10] |
| Stride warping / play-rate | Adjust stride to capsule speed, but keep the correction within about +-15-20%. Beyond that quality collapses and a little slide is better | [S13] (Lyra guidance) |
| Orientation warping | Rotate the lower body toward the travel direction and counter-rotate the upper body to the aim direction; only 4 directional sets are needed | [S13] |
| Additive aim offsets | Author aim poses (up/down/left/right/centre) as additive clips on the base aim pose and blend with a 2D blend space | [S13] |
| Transitions | Inertialization: instead of cross-fading two poses, snap to the new pose and decay the offset with a quintic, which also removes the double evaluation cost | [S12] |
| Motion matching vs blend trees | Motion matching searches mocap by pose and trajectory. It needs lots of data and tooling. For a small procedural set, a blend tree with explicit start/stop/pivot states is the realistic choice | [S11] |

### 1.6 Real firearm handling (technique level, for animation believability)

**Stances**
- Isosceles: squared to the target, feet about shoulder width, both arms extended, slight forward lean with knees bent, support-side foot slightly forward in the modern version [S15].
- Weaver: bladed, strong arm near straight, support elbow bent and low [S15].
- Rifle: stock in the shoulder pocket (just above the collarbone, below the shoulder joint), pressed firmly so recoil spreads through the body; cheek on the stock; weight slightly forward, knees soft, elbows not locked [S20].
- Support hand on the handguard, support elbow pointing down. This is where the bladed look comes from.

**Carries** [S16]
- Low ready (rifle): stock in the shoulder, muzzle depressed about 45 deg (some use 15-20 deg), raised to engage.
- High ready: muzzle about 45 deg up, stock tucked under the arm, then driven out and onto the shoulder.
- Compressed ready (pistol): elbows bent and against the ribs, muzzle forward and slightly down (or up no more than ~45 deg).

**Walking while aiming** [S17]
- "Groucho" walk: knees bent, lower centre of gravity, slight forward lean at the waist, heel-to-toe rolling steps, short steps. Goal is almost no upper-body bounce ("book on your head"). Backwards: toe-heel. Hips, knees and toes point in the direction of travel. Larger steps increase vertical sway.
- Animation consequence: aimed walking has **no flight phase**, a small vertical bob, a bladed torso that stays stable, and the legs do the moving.

**Recoil** [S20]
- Good shooters keep splits around 0.15-0.25 s (0.33 s for slower/precision). The body absorbs recoil with locked wrists, tight shoulder contact and forward weight; the muzzle lifts then returns before the next shot. I found no sourced angle; I use 2-3 deg for a pistol, 1.5-2.5 deg for a rifle, 5-6 deg for a shotgun **(est.)**.

**Reload timings**

| Reload | Typical time | Source |
|---|---|---|
| Pistol speed reload (empty gun, new mag) | 1.2-1.5 s advanced; 1.5-1.8 s club; 2.5-3 s typical carry shooter | [S18] |
| Pistol tactical reload (keep the partial mag) | 2-3 s when it goes right; 5 s or more in practice | [S18] |
| Rifle emergency reload (bolt locked back) | 1.1-1.5 s pro; 1.5-2.5 s experienced; ~3 s typical | [S18] |
| Pump shotgun, whole tube | About 1.7-2.5 s per shell including the shots (Valhalla drill: 6 shells in 10 s is exceptional, 15 s acceptable) | [S19] |
| Pump shotgun, per shell **(est.)** | 0.6-1.0 s per shell for a clean load; rack/pump 0.3-0.5 s | derived from [S19] **(est.)** |
| Weapon swap (draw sidearm or shoulder long gun) | I found no sourced figure. Real draws are about 1-1.5 s; game-feel target 0.4-0.6 s **(est.)** | **(est.)** |

Reload choreography (the support hand and eyes) [S18]: the shooting hand holds the gun near the chest in the eye line; the support hand goes to the belt or pouch, grips the mag with the index finger along the front, brings it up to the magwell, seats it with a push, then the weapon is readied (slide release or charging handle). The head stays mostly up and on the target; the gun comes up toward the eyes, not the eyes down to the gun.

### 1.7 Zombie and creature locomotion in games [S21]

- Asymmetric gait: one leg steps longer, one foot drags, the head lags the body by a beat, a shoulder drops, arms reach with unequal swing.
- Keep intentional drag in the performance; remove only unwanted slip at the planted foot.
- Counter-rotation of hips and shoulders collapses (loose torso).
- A full set needs idle, shamble, fast shamble/jog, run, start and stop, and turn variants [S21].
- Melee lunge timing: a readable anticipation (arms back, weight back) of about 0.25-0.4 s **(est.)**, a fast contact stroke of 3-5 frames (0.1-0.17 s) **(est.)**, then a slow recovery. Check how far the body advances during the attack, since that decides reach and stop distance [S21]. Unpredictable stops and head tilts before the attack add dread [S21].

---

## 2. System recommendations for Unity (Animator + Humanoid, no plugins)

1. **Author each locomotion clip at the speed it is played at. Do not time-scale by more than +-15-20%** [S13]. Blend trees should have several authored speeds (walk, aim walk, jog, run, sprint), not one clip stretched.
2. **Aimed movement uses non-flight gaits.** Cap aim speed near 2.0 m/s so it can be a real walk (walk-run transition 2.0 m/s [S4]) with a Groucho-style low bob [S17]. If aim speed must stay high, author a dedicated "aim jog" with a small bob and a stiff torso rather than reusing the free jog.
3. **Give the character inertia in gameplay code, not only in the clips.** Lower acceleration and a longer deceleration, smoothed turning (no `RotateTowards` at 900 deg/s), and a procedural lean (forward on acceleration, back on braking, banking into turns) driven from velocity change [S10].
4. **Base-layer torso must survive the weapon layer.** The weapon layer should own the arms, fingers and aim direction. The pelvis and spine should keep the run bob and counter-rotation from the base layer. In Unity terms: do not mask in `AvatarMaskBodyPart.Body` for gait-compatible weapon holds, and take the spine direction from the existing LateUpdate aim code. **Verify** what `Body` includes in your Unity version (the API lists Body, Head, arms, fingers, hand IK, foot IK, legs, Root as separate parts [S14]; Body is the hips and spine chain in practice).
5. **Aim offset instead of bone overrides, where cheap.** The current `Rotate(_spine/_chest)` loop with 4 iterations is acceptable, but add smoothing (it should follow the camera with a 0.05-0.08 s lag, not instantly) and keep corrections additive. A proper additive aim-offset blend space (up/down/left/right) is better, but only worth the effort if the LateUpdate loop keeps causing wobble.
6. **Transitions:** keep fixed-duration transitions short (0.1-0.2 s) between states of similar poses, and use code-driven damping on parameters instead of long blends. For jump/land use a distinct state only when stationary; while moving, apply a procedural pelvis dip (inertialization-like decay [S12]) instead of a stationary land clip.
7. **Diagonals:** Unity's 2D freeform blend between Walk-forward and Strafe produces mush because the feet face different ways. `Loco(dir=(dx, dz))` already supports any travel direction, so author the four diagonals (about 45 deg) for aimed movement; no orientation-warping tool is needed.
8. **Foot sync across a blend tree.** Every clip in one tree must start at the same foot contact (left contact at 0). Blending a 62% stance walk with a 34% stance jog is acceptable only where the blend zone is narrow. Prefer a gap of authored speeds no wider than about 1.2-1.5 m/s.
9. **Reload and swap are upper-layer states with events.** Use animation events (or normalized time checks) to hide and show the gun and the magazine at the support-hand contact points, and clamp `ReloadSpeed` to 0.8-1.3 so a hand motion never plays rushed.
10. **Recoil is additive and procedural** in `PlayerAnimation`: chest and head pitch kick plus a small shoulder pull-back, critically damped over 0.12-0.2 s [S20].
11. **Terrain foot IK is not needed** for the flat city. Keep `iKOnFeet`, which only pins feet to the retargeted foot goals.
12. **Motion matching:** not recommended here [S11]. It requires large clip libraries.

---

## 3. Gap analysis

### 3.1 Authored cadence versus played cadence (the numbers)

`SceneBuilder.Animation.cs` plays every clip at `speed / authoredSpeed` time scale. The table is computed from `T` and `speed` in `make_animations.py` and the tree children.

| Clip, tree position | Authored | Played | Cadence played | Step length | Research target | Verdict |
|---|---|---|---|---|---|---|
| Walk @ 1.6 m/s | T27, 1.6 m/s, 133 spm | x1.0 | 133 spm | 0.72 m | about 120 spm, 0.8 m **(est.)** | Fine (about 10% quick) |
| Jog @ 2.75 (aim speed) | T20, 3.6 m/s, 180 spm | x0.76 | 137 spm | 1.2 m | Jog at 2.75 m/s should be about 165 spm, 1.0 m [S2]; or a walk at 2.0 m/s | Slow-motion jog with flight phase at gun-carrying speed; bouncy |
| **Jog @ 5.0 m/s (default move)** | T20, 3.6 m/s | **x1.39** | **250 spm** | 1.2 m | **about 185 spm, 1.6 m [S2]** | **Cadence 35% too high: "hummingbird" legs.** Fast-forward look. Largest single cause |
| Sprint @ 8.5 | T15, 8.0 m/s, 240 spm | x1.06 | 255 spm | 2.0 m | 246-264 spm at 8 m/s, steps 1.84-1.95 m [S3] | Plausible for an athlete. Slightly short step, fine |
| WalkBack @ 1.4 | T30, 120 spm | x1.0 | 120 | 0.7 m | | OK (not used by the player at this speed) |
| **WalkBack @ 2.75 (aim back)** | T30, 1.4 m/s | **x1.96** | **235 spm** | 0.7 m | about 140-150 spm, step ~1.1 m **(est.)** | **Frantic backpedal** |
| Strafe @ 1.6 | T18, 200 spm authored | x1.0 | 200 spm | 0.48 m | about 120-180 spm **(est.)** | Authored already too busy |
| **Strafe @ 2.75 (aim strafe)** | T18, 1.6 m/s | **x1.72** | **344 spm** | 0.48 m | about 150-180 spm; side-shuffle speed realistically <= 2.0 m/s **(est.)** | **Blur. Worst offender** |
| Shamble @ 1.6 | T26, betaL 0.70 / betaR 0.58 | x1.0 | 138 spm | 0.7 m | 80-110 spm for a dragging gait **(est.)** | A bit brisk for "eerie" |
| EnemyJog @ 3.2 | T20 | x1.0 | 180 spm | 1.07 m | 168-176 spm [S2] | Good |
| EnemySprint @ 5.0 | T17 | x1.0 | 212 spm | 1.4 m | about 185 spm [S2] | Slightly high |

Unity's blend-tree time scale also changes linearly with the blend weight, so in the 1.6 to 2.75 m/s zone the walk (133 spm) and jog (137 spm) mix at different stance proportions (62% vs 34%). That is a second source of foot mush (the foot-slide agent is working on slide, but the cadence and phase mismatch are authoring problems).

### 3.2 Per clip

| Clip / system | Findings versus research |
|---|---|
| **Idle** (T120) | Subtle weight shift, breathing, head stabilisation 0.6. Good. Breathing is 2 cycles per loop; fine. No idle variants (look around) **(est.)**; low priority |
| **Walk** | Stance 62% matches [S1]. Pelvic yaw 5 (target +-4 [S5]), tilt 3 (target ~5 deg drop). Bob `bobA=0.04` (amplitude) is 8 cm peak-to-peak against about 5 cm [S5]: about 60% too bouncy. Sway 3 cm amplitude (6 cm p-p) is fine. Elbow 24 +- 9 (target nearly straight, 10-25) is fine. Arm swing 18 front/24 back reasonable |
| **Jog** | Stance 34% (target ~35-40% [S6]), forward lean 9, elbow 88 (matches [S7]), `stab` 0.75, pelvic yaw 9. Contact-sheet frames 2, 5 and 12 show deeply flexed stance knees and a low pelvis; target is about 40 deg flexion at mid-stance and 15-20 deg at contact [S6]. Run `knee_stats(JOG)` to check; **verify**. Authored at 3.6 but played at 5.0 (see 3.1). Hands are held as loose fists in front of the chest; the arms barely look like they swing in the side view |
| **Sprint** | Stance 22% (matches [S6]), lean 22 (est. range 10-20, slightly high), elbow 90. Good. Authored at 8.0, played at 8.5 |
| **WalkBack** | Authored for 1.4 m/s, played at up to 2.75 (235 spm). Needs a dedicated aim-speed backward gait |
| **StrafeLeft/Right** | Cross step 0.16, `swing` 8, T18 means 0.96 m stride; at 2.75 m/s it is a blur. No diagonals (see 2 item 7) |
| **Jump / Fall / Land** | `PlayerController` applies the jump impulse at once (9.2 m/s, 0.66 s airtime). `JumpStart` spends its first 0.20 s in an anticipation crouch (hips -0.22 m) while the capsule is already rising, then extends at 0.30 s; the crouch and the launch are out of sync. `Land` is a stationary stand-pose clip (feet at `stand_feet`) that plays at 0.5 s even if the player lands at 5 m/s, so the legs stop while the body moves. The land exit time is 0.5 with a 0.2 s blend: input response is delayed. Fall pose is fine |
| **AimPistol** | Static pose (breathing only). Two-handed isosceles-ish, lean 7, head -6: matches [S15]. No sway or weapon sag; no recoil clip (only a 3 cm gun-mount slide, decaying in about 0.11 s) |
| **AimRifle** | Bladed chest (-35 yaw in clip, plus `bladeYaw` 28 in LateUpdate **verify they are not double counting**), stock in pocket (`pocket` offset), cheek weld via head roll -9. Matches [S20]. Static; no weight shift |
| **HoldRifleLow** | Muzzle ~38-40 deg down (`lowReadyPitch` 40): matches [S16]. Static: it is a hold, so while the character runs the arms/torso do not follow the run bob if the layer masks the Body |
| **ReloadPistol** (42 f, 1.4 s) | Support hand goes to a "hip" pouch point, up to the magwell, seats it. Matches [S18] in structure. Missing: no slide release/rack at the end, magazine eject is not shown as a motion of the gun (mag parts move through `gun spec`). 1.4 s is faster than the 1.5-1.8 s club speed [S18]; if `ReloadSpeed` raises it further it reads as rushed. Head moves -2 to 14 deg down, so it looks at the gun; real technique keeps the eyes up [S18] |
| **ReloadRifle** (69 f, 2.3 s) | Good structure: lowers rifle, hand to chest pouch, magwell, charging-handle pull at 1.92-2.10 s, shoulder again. 2.3 s matches 1.5-2.5 s experienced [S18]. The same clip is used for the shotgun, which is wrong (a pump uses tube loading) |
| **Shotgun reload** | No clip. The pump shotgun uses `ReloadRifle`. Real behaviour: 0.6-1.0 s per shell, then a pump [S19] |
| **Weapon swap** | No animation. `RefreshGun()` toggles the gun models on `WeaponChanged`, and the upper layer blends between hold states over 0.2 s. The gun pops in |
| **Death** (54 f) | Keyed fall with grounded hips. Fine. Starts from the standing pose, not from the current moving pose, so a death while sprinting blends over 0.15 s from a run; the body then stops dead (no momentum) **(est.)** |
| **Enemy Idle/Shamble** | Asymmetry exists (`betaL` 0.70 vs `betaR` 0.58, `offR` 0.47, shoulder drop, head lolling, arm reach). Matches [S21]. Brisk for a shambler (138 spm); no foot drag in the swing (`toe_swing` -10 and `clear` 0.07 are small but not a drag); no hitch |
| **EnemyJog / Sprint** | Lean 16/26, reaching arms, head lag (`stab` 0.2). Good silhouette |
| **EnemyAttack** (33 f) | Windup 0.22-0.34 s (arms up, weight back), contact 0.45, follow-through 0.58, recovery to 1.1 s. Total contact timing and recovery match [S21]. The body lunges only through hips offsets (hips -0.26 m): no root displacement, so it reads as an in-place lean. Attack starts from the hunched idle pose, not from the current stride, so the legs freeze for 0.15 s transition then plant |
| **EnemyHit** (11 f) | Upper-body flinch layer, 0.9 s cooldown. Good. The mask includes `Body` so the spine is fully replaced during the flinch; fine for 0.37 s |

### 3.3 Controller, blend setup and overrides

| Area | Findings |
|---|---|
| `PlayerController` acceleration 40 m/s^2 | 0 to 5 m/s in 0.125 s, 8.5 m/s in 0.21 s, and stopping from 5 m/s in 0.125 s. No start or stop animation can exist at that rate; the character appears to teleport into a gait. Real runners need seconds to reach top speed; for games, 0.25-0.4 s to run speed reads as responsive but weighty **(est.)** [S10] |
| Rotation `turnSpeed` 900 deg/s with `RotateTowards` | A 180 turn takes 0.2 s and is linear, then hard-stops. No plant-and-turn, no lean, no overshoot [S10]. With the camera-relative wish direction, every WASD change turns the whole body that fast |
| `moveDamp` 0.08 and `LocoSpeed` damp 0.1 | OK for smoothing parameters but with acceleration 40 the damp is the only thing smoothing the legs; the blend tree reacts to velocity in 80 ms |
| 2D freeform on local velocity | Right idea. Problems: aim-speed children use the free-jog clip (flight phase at 2.75 m/s with a gun); strafe/back are over-time-scaled; no diagonals; walk and jog stance ratios differ |
| Layer "UpperBody" (Override, mask = Body + Head + Arms + Fingers + HandIK) | When a long gun is held, the layer weight is 1 at all times (`pistolRelaxedWeight` 0 only gives the pistol a free-arm run). `Body` in the mask replaces the pelvis and spine with the aim/hold clip, so the base layer's bob, pelvic rotation, lean, and counter-rotation do not appear on a long-gun run. Effect: legs bob, torso is frozen (looks like a wooden doll skating). Also the aim clip hip height (-0.03) overrides the jog hip height **(verify)** |
| Procedural overrides in `LateUpdate` | `Rotate(_spine/_chest)` with 4 passes, `Blade` (adds 28 deg chest yaw each frame), `ShoulderGun` (two-bone IK on the right arm), `PlaceLeftHand`, `RollWrists`, finger curls. These are correct tools but: (a) they run on the bones already driven by the layer clip, so the clip and the code both set chest yaw (possible double count); (b) the aim target (camera ray, min distance 10 m) is applied at `_aimBlend` 8/s with no smoothing, so camera moves transfer straight to the torso; (c) pitch is applied to the spine instead of additive aim poses, which bends the whole torso for a downwards aim |
| `kick` | 3 cm gun-mount translation. No body response [S20] |
| Reload speed | `ReloadSpeed = clipLength / weapon.ReloadSeconds` unbounded (clip 1.4 s/2.3 s). A 0.8 s weapon gives x1.75 |
| Enemy controller | Simple 1D speed blend with time scaling in a +-8% window around nominal speeds: fine and within [S13]'s limits. Enemy flinch mask = Body: OK. `CycleOffset` random phase: good |
| Foot sliding | Another agent is on this. Note only that all the cadence issues above get worse the more a clip is time-scaled |

### 3.4 Specific causes of the clunky feel (ranked)

1. **Cadence/stride mismatch from time-scaling** (jog 250 spm, aim strafe 344 spm, aim back 235 spm). The legs run at a rate no human uses [S2][S13].
2. **No inertia anywhere: acceleration 40, linear 900 deg/s snap turns, no lean, no start/stop.** The pose changes faster than the body can, so every direction change pops [S10].
3. **The weapon layer's Body mask and LateUpdate overrides freeze the torso** so the legs run under a stiff upper body, plus no recoil body response, no swap, a stationary Land clip while moving, and a Jump clip whose crouch lags the launch.

---

## 4. Prioritized fix list (impact versus effort)

Targets are **(est.)** unless cited. Frame counts are at 30 fps.

| # | Change | Where | Target values | Effort |
|---|---|---|---|---|
| 1 | **Author the clips at the speeds they are played** and stop time-scaling | `make_animations.py` (`JOG`, `SPRINT`, `WALKB`, `strafe`, new `RUN5`, `AIMWALK`) and `SceneBuilder.Animation.cs` `BuildPlayerController` (`AddLocoChild`) | Run: `Loco(T=19, speed=5.0, beta=0.32, lean=11, bobA=0.03)` = 190 spm, step 1.58 m [S2]. Keep current `JOG` as the 3.6 m/s child (180 spm [S2]). Sprint: `speed=8.5, T=15` (240 spm, step 2.1 m [S3]). Tree: Idle 0, Walk 1.6, Walk-fast 2.0 (T=28), Jog 3.6, Run 5.0, Sprint 8.5, all with timeScale between 0.85 and 1.2 | M, highest impact |
| 2 | **Slow the aim speed to a real walk and author aim gaits for it** | `PlayerController` `aimSpeedScale` 0.55 to 0.40 (aim speed 2.0 m/s [S4]); new clips `AimWalkBack` (T26 at 2.0, back=True, lean 3, bobA 0.012) and `AimStrafe` (T20 at 2.0, bobA 0.012, cross 0.2); constants `PlayerAimSpeed` 2.0 in `SceneBuilder.Animation.cs` | Bob 2.4 cm p-p (Groucho [S17]), no flight phase, knees ~25-35 deg flexed throughout (`drop` about 0.04), `stab` 0.9 | M |
| 3 | **Add inertia to movement** | `PlayerController.Update`: replace the single `acceleration` with `acceleration` 18 and `deceleration` 26 (use `MoveTowards` rate chosen by whether `wish` magnitude is rising or falling); replace `RotateTowards(...900)` with `Mathf.SmoothDampAngle` on yaw, smoothTime 0.07 s while moving, and keep `turnSpeed` 900 only in `combat` | 0 to 5 m/s in 0.28 s; 5 to 0 in 0.19 s; 180 turn about 0.25 s with ease-out [S10] | S |
| 4 | **Procedural lean** (forward on acceleration, back on braking, bank into turns) | `PlayerAnimation.LateUpdate`, new `ApplyLean()` before the aim code, rotating `_spine` about `_root.right` and `_root.forward`; needs acceleration and yaw rate from `PlayerController` | Pitch = clamp(accelAlongForward x 0.35 deg per m/s^2, -8, +10); roll = clamp(-yawRate(deg/s) x speed x 0.002, -8, 8) deg, smoothed with 0.08 s damping; fade to zero when aiming [S10] | S |
| 5 | **Stop the weapon layer from freezing the torso** | `SceneBuilder.Animation.cs` `BuildUpperMask`: set `Body` to false (keep Head, arms, fingers, hand IK). The spine is already aimed in `PlayerAnimation.LateUpdate` (`Rotate`, `Blade`). Then rebuild controllers and check the pistol and rifle holds | Expected: run bob, pelvic rotation and lean visible while holding a rifle. Check `bladeYaw` is not double counted with the clips' `yaw=-35` (reduce clip yaw to 0 if so). If `Body` cannot be separated from the Hips for humanoid masks, instead blend the layer weight to 0.6 while moving above 1 m/s **(verify)** | S (verify) |
| 6 | **Jump and land fixes** | `make_animations.py` `jump_keys`: delete the crouch key at 0.20 s so the clip starts in the drive pose (about 5-6 frames before leaving the ground), and shorten to 10 frames; controller: Fall to Land only when `speed < 1 m/s`; otherwise Fall to Locomotion (0.1 s) and add `PlayerAnimation` `OnLanded` event handling a pelvis dip | Pelvis dip = clamp(impactSpeed x 0.025, 0.04, 0.16) m down in 0.07 s, back up in 0.22 s (critically damped); `Land` exit time 0.35 | M |
| 7 | **Additive recoil on the body** | `PlayerAnimation.OnFired` and `LateUpdate`: add a decaying chest/head pitch kick and shoulder pull-back to the existing `_kick` | Pistol 2.5 deg pitch, 3 cm back; rifle 2 deg, 4 cm; shotgun 5.5 deg, 6 cm. Decay with critical damping over 0.15 s (matches 0.15-0.25 s splits [S20]) | S |
| 8 | **Weapon swap animation** | New `Swap` state in the UpperBody layer (clip authored in `make_animations.py` from the low-ready pose: gun down and across, support hand moves, gun back up); `PlayerAnimation.RefreshGun` calls the model switch from an animation event at the midpoint | 0.45 s total (14 frames): 6 frames lower, model switch at frame 6, 8 frames raise [est.] | M |
| 9 | **Shotgun reload clip and reload polish** | New `ReloadShotgun` clip: start 0.3 s, then a per-shell loop of 0.7 s (hand to belt, shell to the tube, push), then pump 0.4 s [S19 est.]; `PlayerAnimation.Update`: clamp `ReloadSpeed` to 0.8-1.3; pistol reload add slide release at 1.1-1.3 s (support hand brushes the slide, 4 frames) and keep the head up (`head` -2 instead of +12/14) [S18] | | M |
| 10 | **Diagonals and enemy polish** | `make_animations.py`: four aimed diagonal clips with `Loco(dir=(+-0.707, +-0.707))` at 2.0 m/s and add children at the diagonals in the tree. Enemy: `SHAMBLE` T 26 to 34 (cadence ~105 spm), `clear` 0.04 with a late heel drag on the longer-stance leg (increase `betaL` to 0.72), `EnemyAttack`: add 0.6-0.9 m forward root slide between 0.32 and 0.50 s from `Enemy` code, hold the anticipation 0.1 s longer | | M |

Suggested order of work: 3, 5 and 4 (small code changes, big feel gain), then 1 and 2 (re-author clips), then 6, 7, 8, 9, 10.

Quick verification list in Play Mode: (a) jog at full speed and count foot contacts per second (should be about 3.1 per second per foot pair step rate, i.e. about 185 spm); (b) hold a rifle and run: does the torso bob and twist; (c) tap A then D: does the body lean and does the turn ease; (d) land while moving: do the legs keep running.

---

## 5. Things I could not verify

- The walk ratio value and exact walking cadence per speed (the paper fetch returned 403); the 0.0065 figure is my estimate.
- Muzzle-rise degrees and shotgun per-shell timing have no sourced numbers; values are judgement.
- Which bones `AvatarMaskBodyPart.Body` covers in Unity 6 humanoid layers; the manual lists the body-part categories only [S14].
- Knee flexion numbers of the authored clips: `knee_stats()` exists in `make_animations.py` but Blender was not run.
- Frame-level look of in-game captures.

---

## 6. Sources

- [S1] Gait phase percentages and joint angles: https://www.physio-pedia.com/The_Gait_Cycle , https://www.tekscan.com/blog/medical/gait-cycle-phases-parameters-evaluate-technology , https://www.myphysiohelp.it/en/gait-biomechanics/ , https://compedgept.com/blog/gait-analysis-the-8-phases-of-gait/ (sources differ by a few percent on boundaries)
- [S2] Cadence vs running speed: https://pmc.ncbi.nlm.nih.gov/articles/PMC12222555/
- [S3] Sprint step frequency and length: https://media.isbweb.org/images/conf/2003/longAbstracts/HUNTER_186-193_SPO_LONGE.pdf , https://pmc.ncbi.nlm.nih.gov/articles/PMC6784208/ , https://pmc.ncbi.nlm.nih.gov/articles/PMC7739839/
- [S4] Walk-run transition: https://en.wikipedia.org/wiki/Transition_from_walking_to_running , https://spot.colorado.edu/~kram/walk-run.pdf
- [S5] Determinants of gait (pelvic rotation, obliquity, vertical CoM): https://www.semanticscholar.org/paper/The-major-determinants-in-normal-and-pathological-Saunders-Inman/1613ec5693a1e808d14ba0800b2ddd48063d03bf , https://www.sciencedirect.com/science/article/abs/pii/S0021929014000888
- [S6] Running kinematics: https://www.niccostiff.co.uk/wp-content/uploads/2020/02/Biomechanics-of-running-gait.pdf , https://compedgept.com/blog/four-phases-of-sprinting-mechanics/ , https://digitalcommons.sacredheart.edu/cgi/viewcontent.cgi?article=1001&context=pthms_honors
- [S7] Arm swing, elbow and head: https://journals.biologists.com/jeb/article/222/13/jeb197228/2704/Straight-arm-walking-bent-arm-running-gait , https://dx.doi.org/10.1242/jeb.024927
- [S8] Walking cadence ranges: https://www.ncbi.nlm.nih.gov/pmc/articles/PMC7654058/ , https://www.ncbi.nlm.nih.gov/pmc/articles/PMC8296345/
- [S9] Walk cycle key poses and frame timing: https://animation.monmouth.edu/instruct/animation/walk-cycle/ , https://animationexplainers.com/walk-cycle-animation/
- [S10] Game animation craft (starts, stops, pivots, turning circles, leaning): https://www.gameanim.com/ , https://www.gamedeveloper.com/production/the-12-principles-of-animation-in-video-games , https://www.wolfire.com/blog/2013/04/GDC13-Summary-Animation-Bootcamp-Part-4-6/
- [S11] Motion matching: https://www.gdcvault.com/play/1023280/Motion-Matching-and-The-Road , https://www.gameanim.com/2017/01/24/motion-matching-future-gameplay-animation-today/
- [S12] Inertialization: https://www.gdcvault.com/play/1025331/Inertialization-High-Performance-Animation-Transitions , https://media.gdcvault.com/gdc2018/presentations/bollo_david_inertialization_high_performance.pdf
- [S13] Pose, stride and orientation warping, aim offsets, +-15-20% play-rate guidance: https://dev.epicgames.com/documentation/en-us/unreal-engine/pose-warping-in-unreal-engine , https://www.unrealengine.com/en-US/tech-blog/adapting-lyra-animation-to-your-ue5-game
- [S14] Unity avatar mask and blend tree options: https://docs.unity3d.com/Manual/class-AvatarMask.html , https://docs.unity3d.com/ScriptReference/AvatarMaskBodyPart.html , https://docs.unity3d.com/Manual//BlendTree-AdditionalOptions.html
- [S15] Pistol stances: https://blog.primaryarms.com/guide/the-basic-pistol-shooting-stances-explained/ , https://en.wikipedia.org/wiki/Isosceles_Stance , https://en.wikipedia.org/wiki/Weaver_stance
- [S16] Ready positions: https://www.thearmorylife.com/ready-up-three-ar-rifle-presentation-techniques/ , https://premierbodyarmor.com/blogs/pba/low-ready-position , https://www.swatmag.com/article/situational-handgun-ready-positions-another-perspective/
- [S17] Groucho walk: https://sofrep.com/news/how-to-simultaneously-walk-and-shoot-like-a-pro/ , https://coachdavidalexander.wordpress.com/2015/12/29/self-defense-shooting-groucho-walk/ , https://www.officer.com/training-careers/article/10248338/get-off-the-x
- [S18] Reload times: https://airsoftshottimer.com/en/posts/magazine-reload-guide/ , https://crateclub.com/blogs/loadout/how-long-does-it-take-to-reload-an-assault-rifle , https://www.handgunsmag.com/editorial/tactics_training_treload_061604/138961 , https://gundigest.com/more/how-to/firearm-training/tactical-reload
- [S19] Pump shotgun loading: https://www.ammoland.com/2026/07/the-valhalla-drill-mastering-speed-reloads-with-shotguns/ , https://www.shootingillustrated.com/content/5-must-know-drills-for-your-home-defense-shotgun/
- [S20] Recoil handling and shoulder pocket: https://boondocksfta.com/2021/09/tips-to-improve-your-defensive-shooting/ , https://flatlinefiberco.com/how-to-manage-rifle-recoil-for-smoother-shooting/ , https://g2precision.com/science-recoil-management-ar15/
- [S21] Zombie locomotion: https://mocaponline.com/blogs/mocap-news/horror-game-animation-guide , https://www.animationshopee.com/post/choose-zombie-animation-clips-complete-movement-set

---

## Clip contract v2 (orchestrator, for the animation pass)
The Blender clip agent produces exactly these files; the Unity systems agent consumes exactly these names.
All are `Assets/Art/Animations/Anim_<Name>.fbx`: armature-only, on `bl_skeleton`, 30 fps, in place
(no net XZ root travel), seamless where looping. Speeds are the in-game speeds they will be played at
(time-scale them by at most ±15%). Foot plants must hold with Foot IK on.

| Clip | Loop | Speed / length | Notes |
|---|---|---|---|
| `Run` | yes | 5.0 m/s, ~190 steps/min | the default move; replaces time-scaled Jog at 5.0 |
| `Sprint` | yes | **8.5 m/s** (retuned from 8.0) | |
| `AimWalk` | yes | 2.0 m/s forward | no flight phase; ~2.4 cm peak-to-peak bob; rolling heel-to-toe; slight knee bend |
| `AimWalkBack` | yes | 2.0 m/s | |
| `AimStrafeLeft`, `AimStrafeRight` | yes | 2.0 m/s | side-steps, no scissor-crossing |
| `AimWalkFL`, `AimWalkFR`, `AimWalkBL`, `AimWalkBR` | yes | 2.0 m/s diagonals | hips twist toward the travel direction, chest stays toward the aim |
| `JumpStart` | no | ~0.17 s | **no 0.2 s pre-crouch**; push-off only |
| `PistolCarry` | yes | upper body | compressed/low-ready pistol carry while moving: both hands, gun close to the chest, muzzle down-forward. Replaces the swinging one-handed pistol |
| `WeaponSwap` | no | 0.45 s (14 frames) | upper body; the model switch happens at **frame 6** |
| `ShotgunLoadStart`, `ShotgunLoadShell`, `ShotgunLoadEnd` | no / yes / no | ~0.35 s, ~0.70 s per shell, ~0.45 s with pump | tube loading; the shell clip loops once per shell |
| `Shamble` | yes | 1.6 m/s, **~105 steps/min** | uneven rhythm, heel/toe drag on one foot, head and shoulder lag |
| `EnemyAttack` | no | 1.1 s, contact at 0.45 s | a longer anticipation hold; root slide is applied by Unity code (0.6–0.9 m), not in the clip |

The existing clips (Idle, Walk, Jog, WalkBack, StrafeLeft/Right, Fall, Land, AimPistol, AimRifle,
HoldRifleLow, ReloadPistol, ReloadRifle, Death, EnemyIdle, EnemyJog, EnemySprint, EnemyHit,
EnemyDeath) stay; improve them in place only where the gap analysis says so.
