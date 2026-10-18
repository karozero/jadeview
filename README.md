# JadeView

[Русская версия](README.ru.md)

Author: ARK [Discord profile](https://discord.com/users/355708121676644353). Published with the author's permission.

Questions? Join the [Discord server](https://discord.gg/KYsaVU9rg).

A Windows asset viewer and scene editor for **Beyond Good & Evil (2003, PC)**. Opens `sally_clean.bf` archives in the original BIG `0x22` format; HD and console archives are not supported.

## Run

Requires Windows, the **.NET 10 SDK**, and your own game archive.

```powershell
dotnet run --project JadeView.csproj -- "C:\Games\Beyond Good & Evil\sally_clean.bf"
```

You can also run without an argument and use **Open BF**. To build: `dotnet build -c Release`.

## Usage

1. Open the **_sally_clean.bf_** file. Choose a scene and, where available, a world in the **Outliner**. You can also double-click an FF0 container in the archive catalog.
2. Select an object in the viewport or Outliner. Inspect its resources and transform in **Details**; browse textures, audio, text and diagnostics in the content browser.
3. Move supported static objects with the move gizmo or position fields; change rotation in **Details**. Scaling and editing skeletal, animated or parented objects are currently disabled.
4. Use **Save project** to save a `.bgemod` project, **Open project** to reopen it, and **Build BF** to create a separate modified archive. Saving a project does not modify the game archive.
5. Use **Export** for the active world, or an object's context menu for that object, to export **GLB**. Export options include textures, actors and animations. Texture, audio and text tabs provide **PNG**, **WAV** and **JSON** export respectively.

Animation controls support clip selection and playback for recognized actors. **PLAY (BGE)** can install the current scene modifications into the game archive and launch the game; **Restore original BF** restores its backup.

## Controls

| Input                            | Action                                                         |
| -------------------------------- | -------------------------------------------------------------- |
| Left click                       | Select an object; drag a move-gizmo axis to translate          |
| Double-click in Outliner         | Select and focus an object                                     |
| Hold right mouse + move mouse    | Look around                                                    |
| Hold right mouse + W / A / S / D | Fly forward / left / backward / right                          |
| Hold right mouse + Q / E         | Fly down / up                                                  |
| Left Shift while flying          | Move faster                                                    |
| Mouse wheel (outside flight)     | Move camera forward / backward                                 |
| W (outside flight)               | Select move gizmo                                              |
| F                                | Focus selection                                                |
| G (viewport focused)             | Toggle game view (hide editor overlays and collision geometry) |
| H / Shift+H / Alt+H              | Hide selection / isolate selection / show all                  |
| Ctrl+Z / Ctrl+Y                  | Undo / redo                                                    |
| Ctrl+S                           | Save mod project                                               |

Global editing shortcuts are inactive while editing a field or flying. Hide/isolate changes affect preview only. There are currently no E/R rotation/scale gizmo shortcuts.

## Current scope

Supports scene inspection, resource previews, GLB export and limited transform editing. Full asset import/replacement and mesh/material editing are not available in the UI. Some scenes use fallback parsing, and some material/UV effects are approximated during export; check **Diagnostics** and export warnings.
