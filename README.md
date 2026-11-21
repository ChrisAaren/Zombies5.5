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

## Building the game

1. Open the project and run **Tools > Zombies 5.5 > Build Scene** (only needed once per clone).
2. Open **File > Build Profiles**, select your platform, and click **Build**.

### Mac and Linux

Unity only builds for platforms whose build support module is installed.

1. In Unity Hub, open **Installs**, click the gear icon next to Unity 6000.6.9f1, and choose **Add modules**.
2. Select **Mac Build Support (Mono)** and/or **Linux Build Support (Mono)**, then install.
3. Restart Unity. In **File > Build Profiles**, choose **macOS** or **Linux** and click **Build**.

Mac builds made this way are not signed with an Apple Developer account, so macOS warns that the app is from an unidentified developer. To open it anyway, right-click the app and choose **Open**, or run `xattr -cr "Zombies 5.5.app"` in Terminal.

## Controls

These are the defaults. To rebind them, open **Settings > Keybinds**. Every action has a keyboard/mouse slot, an alternate slot and a gamepad slot. Binding a key that is already in use swaps it with the other action. Bindings are saved between sessions.

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

## License

Released under the [MIT License](LICENSE). Unity, its packages, and the third-party assets listed under Credits keep their own licenses.
