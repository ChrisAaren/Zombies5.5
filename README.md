# Zombies 5.5

A third-person zombie survival shooter built in Unity 6 with URP. You fight through rounds of zombies across city districts, buy guns from kiosks, open gates into new zones, and pick up kill drops.

This is simply a game that can be used for testing purposes, something to build off of, or just something to pass the time. 

## Requirements

- Unity **6000.6.9f1** (see `ProjectSettings/ProjectVersion.txt`)
- Universal Render Pipeline. Unity installs it from `Packages/manifest.json` when the project opens.

## Getting started

1. Clone the repo and open the folder in Unity Hub with Unity 6000.6.9f1.
2. Open `Assets/Scenes/Zombies55.unity`.
3. Run **Tools > Zombies 5.5 > Build Scene** once.
4. Press Play.

## Controls

| Action | Keyboard / mouse | Gamepad |
|---|---|---|
| Move | WASD or arrow keys | Left stick |
| Sprint | Shift | |
| Jump | Space | |
| Aim | Right mouse button | Left trigger |
| Fire | Left mouse button | |
| Reload | R | |
| Switch weapon | 1 / 2 or mouse wheel | |
| Buy weapon / open gate | Hold E | X / Square |
| Pause | Esc or P | Start |

## Project layout

| Path | Contents |
|---|---|
| `Assets/Scripts` | Gameplay: player, combat, enemies, rounds, zones, pickups, UI, graphics settings |
| `Assets/Editor` | Scene builder and asset generators (menu: **Tools > Zombies 5.5**) |
| `Assets/Art` | Generated models, materials, textures, animations, FX |
| `Tools/Blender` | Blender scripts for props, guns, FX and animation clips |
| `Tools/CharacterPipeline` | Rigging and skinning pipeline for characters (see its README) |

## Credits

- Test character built with [MPFB](https://static.makehumancommunity.org/mpfb.html) using CC0 MakeHuman assets.
- The motion-capture research in `Tools/Blender/mocap` uses the [100STYLE dataset](https://www.ianxmason.com/100style/) (Ian Mason, Sebastian Starke, Taku Komura; CC BY 4.0) and the [CMU Graphics Lab Motion Capture Database](http://mocap.cs.cmu.edu/). The raw data is not included in this repo. See `Tools/Blender/references/mocap/SOURCES.md`.
