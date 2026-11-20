# Task: build an automated character pipeline (rig + checks) using only Blender and Unity

(The user's specification, saved verbatim, with orchestrator notes at the end.)

## Context
- Unity project: <project root> (Unity 6000.6.9f1). Follow its CLAUDE.md.
- Tools: Blender (headless, `blender -b --python <script> -- <args>`) and Unity. Nothing else: no extra Python packages beyond what ships with Blender (numpy is included), no cloud services, no paid add-ons.
- Goal: take a static humanoid mesh (A-pose or T-pose, ideally with spread fingers) and automatically produce a clean, rigged, game-ready FBX that imports into Unity as a valid Humanoid avatar. Each step must be checkable by numbers and a small number of images, so you can iterate without manual help.
- Look at what already exists in the project and in your Blender workflow first. Reuse and extend it rather than duplicating it.

## Layout
- Scripts: Tools/CharacterPipeline/
- Working files: Tools/CharacterPipeline/work/<character>/v001, v002, ... (a new folder per iteration; never overwrite an earlier version)
- Final output: Assets/Art/Characters/<character>/<character>.fbx

## Stage 1: Mesh cleanup (cleanup.py)
1. Import the source mesh, apply all transforms, put the feet on the ground at the origin, face -Y in Blender, real-world scale (adult ≈ 1.7–1.9 m unless the user says otherwise).
2. Make a watertight **proxy**: copy the mesh and apply a voxel Remesh modifier. Keep the voxel size smaller than the gap between fingers, or the fingers will merge and their weights will bleed. Check this (see Stage 5) and lower the voxel size if needed.
3. Make the **game mesh**: decimate to the polygon budget (ask the user if none is given; default about 15–30k triangles for a main character), UV unwrap, bake albedo and normals from the original high-poly mesh onto it.
Polygon and texture budgets (target: roughly GTA IV-era quality, slightly above):
- Player: 20–30k triangles, 2048 textures
- Story/important NPCs: 10–20k triangles, 1024–2048 textures
- Pedestrians: 5–10k triangles, 1024 textures
- Generate LODs for every character: LOD1 ≈ 50% and LOD2 ≈ 20% of LOD0 triangles (Decimate on the skinned game mesh, keeping vertex groups), exported in the same FBX using Unity's _LOD0/_LOD1/_LOD2 naming so Unity builds the LOD Group automatically.
- check.py: test each LOD's triangle count against its budget, and include LOD1/LOD2 in the stress-pose checks (low LODs tend to break at the elbows and knees first).

## Stage 2: Find the joints (joints.py)
Estimate joint positions from the mesh's shape, then confirm them visually:
- Slice the proxy horizontally at regular heights and count the separate pieces in each slice:
  - where one piece becomes two near the bottom = crotch, which gives the hips/upper legs
  - where arms separate from torso (A-pose) = armpits/shoulders
  - narrowest point between shoulders and head = neck
- Along each limb, take cross-sections perpendicular to the limb direction:
  - elbows and knees: narrowest points near standard body proportions
  - wrists and ankles: narrowest point before the hand or foot
- Hands: slice across the palm and count pieces to get the finger count. Find fingertips at the outermost point of each piece, and place knuckles along each finger at standard proportions.
- If the shape analysis is ambiguous, fall back to standard body proportions and mark that joint as "estimated".
- Write joints.json with a position and a confidence value for each joint.

## Stage 3: Build the skeleton (skeleton.py)
- Create a plain deform-only armature in Python (no Rigify) with Unity Humanoid bone names: Hips, Spine, Chest, UpperChest (optional), Neck, Head, Left/Right Shoulder, UpperArm, LowerArm, Hand, UpperLeg, LowerLeg, Foot, Toes, and three bones per finger (Thumb, Index, Middle, Ring, Little × Proximal/Intermediate/Distal).
- Place the bones from joints.json, making left and right mirror images.

## Stage 4: Skin weights (skin.py)
1. Parent the **proxy** to the armature with Automatic Weights. This works because the proxy is watertight, which is exactly where automatic weighting stops failing.
2. Copy the weights to the game mesh with a Data Transfer modifier (vertex groups, nearest face interpolated), then apply it.
3. Limit each vertex to 4 bone influences and normalize all weights.
4. Export FBX for Unity: only deform bones, no leaf bones, apply transforms, scale set for Unity.

## Stage 5: Checking script (check.py), the core of the iteration loop
One command takes a version folder and writes **report.json** and **sheet.png**.

### report.json (numeric checks, each with pass/fail against a threshold)
- Mesh: holes and broken edges, triangle count against the budget, whether the proxy is watertight
- Proxy fingers: finger count in the proxy should match the source mesh (catches fused fingers)
- Weights: unweighted vertices, vertices with more than 4 influences, weights that don't add up to 1, vertices whose strongest bone is far from them (more than ~15% of body height away, which catches things like hand vertices moved by the spine)
- Skeleton: left/right mirror mismatch, missing required Humanoid bones, joints with low confidence
- Stress poses (fixed set, defined in poses.json): arms raised, arms forward, elbows bent 140°, squat (hips and knees 100°), torso twist 45°, head turn 70°, fist (fingers curled 90°), front kick 90°, leg out sideways 45°. For each pose:
  - volume change compared to the rest pose (large loss = collapsing joint)
  - places where the surface passes through itself (use Blender's BVHTree overlap and ignore neighboring faces)
  - worst edge stretch per body region
- Summary: overall pass/fail and a list of the worst problem areas

### sheet.png (one labeled grid image)
- Render with Workbench (fast, flat shading) at about 512 px per tile, using fixed cameras so iterations can be compared directly.
- Tiles:
  - front and side orthographic views with joint dots and bone lines drawn on top
  - mesh colored by strongest bone (a distinct flat color per bone)
  - every stress pose
  - close-ups of the hands, armpits, crotch and shoulders, framed automatically from bone positions
- Build the grid with numpy and Blender's image functions (no Pillow needed).
- Optional: a side-by-side of the previous version's sheet, for before/after comparison.

## Stage 6: Unity check (Unity editor script, batch mode)
- An import rule (AssetPostprocessor) for Assets/Art/Characters/** that sets the animation type to Human and generates the avatar from the model.
- A check script that imports the FBX and confirms the avatar is valid and humanoid, with all required bones mapped. It then plays a test animation on the character (any humanoid clip in the project, or a generated test pose), takes a screenshot, and writes unity_report.json.

## Iteration loop
1. Run the pipeline into a new version folder.
2. Run check.py. Read report.json first; only open sheet.png when the report flags something, or for the final sign-off.
3. Make **one** targeted fix per iteration (move a joint, change the proxy voxel size, smooth weights in one area, etc.) and write down what changed and why in work/<character>/log.md.
4. If a version scores worse than the one before, go back to the earlier version.
5. Stop when everything passes and the Unity check passes, or after 8 iterations. Then report to the user what still fails, with the sheet.

## Done means
- All scripts run headless with a single command each, and are documented in Tools/CharacterPipeline/README.md.
- The pipeline produces a passing result on one humanoid test mesh (use an existing one in the project, or ask the user for one).
- The final FBX imports into Unity as a valid Humanoid and plays an animation without visible problems.
- Report back: what you built, the test results (report.json summary + final sheet.png), and any thresholds or decisions the user should confirm (polygon budget, character scale).

---

## Orchestrator notes (decisions taken, to confirm with the user at the end)
- **Budgets:** the defaults above (player 20–30k tris, 2048 textures; adult 1.7–1.9 m).
- **Test input:** an unrigged static humanoid generated with MPFB (already installed, CC0 MakeHuman assets) in an A-pose with spread fingers, body and clothes joined into one mesh with its materials and textures: a stand-in for an external sculpt. Character name: `testcourier`.
- **Stage 6:** Unity batch mode cannot open the project while the Editor has it open (project lock). The Unity check runs through the open Editor (`unity command script run` / `script eval`) when one is running, and in batch mode only when the project is closed. Same script, two entry points.
- **Version folders:** `work/<character>/v*/` are gitignored (they're regenerable); each character's `log.md` stays in git.
- **Reuse:** `Tools/Blender/bl_skeleton.py` (Humanoid names, three-bone fingers, consistent rolls), `hr_render.py` (preview rendering), `make_human_verify.py` (FBX reimport check), `bl_common.py` (FBX export conventions), and `make_mpfb_characters.py` (MPFB generation).
