# Mocap sources (raw BVH are git-ignored; this file and .gitignore are tracked)

All BVH files are treated as data only: parsed by `Tools/Blender/mocap/retarget_mocap.py`, never executed.

## 1. 100STYLE (folder `100style/`) - CC BY 4.0
- Origin: https://zenodo.org/records/8127870 (`100STYLE.zip`, 1.5 GB; extracted by HTTP range read, see `mocap/fetch_100style.py`)
- Project page: https://www.ianxmason.com/100style/ : "This work is licensed under a Creative Commons Attribution 4.0 International License." and "For creative or commercial work please credit in an appropriate way." (example: "The 100STYLE Dataset - Ian Mason")
- License: https://creativecommons.org/licenses/by/4.0/ (commercial use and modification allowed with credit).
- Authors: Ian Mason, Sebastian Starke, Taku Komura. 60 fps, cm, 28-bone skeleton, zero-rotation T-pose rest.
- Codes: FW fwd walk, FR fwd run, SW sidestep walk, SR sidestep run, BW back walk, BR back run, ID idle.

| File | Subject/style | Used as |
|---|---|---|
| Neutral_ID.bvh | Neutral idle | Idle |
| Neutral_SW.bvh | Neutral sidestep walk | StrafeLeft / StrafeRight (mirrored) |
| Neutral_SR.bvh | Neutral sidestep run | StrafeRunLeft |
| Zombie_FW.bvh | Zombie walk | Shamble |
| Zombie_FR.bvh | Zombie run | ZombieRun |
| Zombie_SW.bvh | Zombie sidestep | ZombieStrafe |
| Zombie_ID.bvh | Zombie idle | ZombieIdle |
| Old_FW.bvh | Old person walk | OldWalk |
| LimpLeft_FW.bvh, LimpRight_FW.bvh | Limp | LimpLeft, LimpRight |
| Frame_Cuts.csv | clean-segment frame ranges (same zip) | cycle search window |

## 2. CMU Graphics Lab Motion Capture Database, BVH conversion (folder `cmu/`)
- Data: http://mocap.cs.cmu.edu/ (CMU; their HTTPS certificate failed verification from this machine, so the official page itself was not read directly).
- Files fetched from the BVH conversion by Bruce Hahne (cgspeed), mirrored at https://github.com/una-dinosauria/cmu-mocap (path `data/<subject>/<subject>_<trial>.bvh`). 120 fps, T-pose added as frame 0, +Z facing.
- License text (CMU wording quoted in https://github.com/una-dinosauria/cmu-mocap/blob/master/READMEFIRST.txt): "Use this data! This data is free for use in research and commercial projects worldwide." Converter: "CMU places no restrictions on the use of the original dataset, and I (Bruce) place no additional restrictions on the use of this particular BVH conversion." cgspeed page: "The files are free to use worldwide for any purpose."
- Acknowledgement requested: "The data used in this project was obtained from mocap.cs.cmu.edu." (NSF EIA-0196217)
- Caveat: an older CMU statement says you may include the data in commercially sold products but may not resell the data directly, even in converted form. The game ships animation clips inside a product, which fits; do not publish the converted FBX/BVH as a standalone asset pack.
- Trial descriptions from the CMU index (cmu-mocap-index-text.txt):

| File | Subject / trial description | Used as |
|---|---|---|
| 35_01.bvh | subject 35, walk | Walk |
| 07_12.bvh | subject 7, brisk walk | BriskWalk |
| 35_17.bvh | subject 35, run/jog | Jog |
| 09_04.bvh | subject 9, run | Run |
| 16_46.bvh | subject 16, run/jog | Sprint |
| 16_45.bvh | subject 16, run/jog | SprintAlt |
| 136_25.bvh | subject 136, normal walk backwards | WalkBack |
| 136_09.bvh | subject 136, walk crouched | CrouchWalk |
| 104_41.bvh | subject 104, ZombieWalk | ShambleCmu |
| 77_19.bvh | subject 77, limping, hurt right leg | LimpCmu77 |
| 91_16.bvh | subject 91, limp | LimpCmu91 |

## Rejected / skipped
- Mixamo: needs Adobe login. Hugging Face / Kaggle / 4TU re-uploads of CMU list conflicting licenses (CC BY-ND, CC BY-NC); not used.
- No source found with rifle/pistol carry or aim walks, or sprints at 5+ m/s.
