# JadeView - BGE Modding tool

![preview image](preview.png)

[Русская версия](README.ru.md)

Author: ARK [Discord profile](https://discord.com/users/355708121676644353). Published with the author's permission.

Questions? Join the [Discord server](https://discord.gg/KYsaVU9rg).

A Windows modding tool for **Beyond Good & Evil (2003, PC)**: asset viewer, scene editor and BF archive builder. Opens `sally_clean.bf` archives in the original BIG `0x22` format; HD and console archives are not supported.

## Feature status

| Feature | Status |
| --- | --- |
| Scene and resource previews | Working |
| GLB / PNG / WAV / JSON export | Working, with limitations |
| Animation playback | Recognized actors only |
| Move / rotate / add / copy / delete objects | Supported static objects only |
| Object scaling | Editor-added objects only |
| `.bgemod` projects, Undo/Redo | Working |
| Build BF, launch game, restore archive | Implemented; see limitations below |
| Full asset import/replacement, mesh/material editing | Unavailable |
| Editing source actors and hierarchies | Unsupported |
| Rotation/scale gizmos, HD/console archives | Unsupported |

## Run

Download the latest packaged build from [GitHub Releases](https://github.com/karozero/jadeview/releases), extract it, and run `JadeView.exe`. Select your own `sally_clean.bf` with **Open BF**.

## Build

To build from source for **Windows x64**, install the **.NET 10 SDK** on Windows, then open a terminal in the project directory and run:

```powershell
dotnet publish JadeView.csproj -c Release -r win-x64 --self-contained false -o artifacts/publish/win-x64
```

Run `JadeView.exe` from `artifacts/publish/win-x64`. This smaller package requires the **.NET 10 Desktop Runtime (x64)** on the target machine and includes native dependencies only for Windows x64.

For a standalone package that includes .NET, publish to a separate directory:

```powershell
dotnet publish JadeView.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64-standalone
```

The standalone package is larger but does not require a separate .NET installation. Distribute the entire chosen publish directory, not the whole `bin` folder. `dotnet publish` restores dependencies automatically.

To start the source build with your game archive, run:

```powershell
dotnet run --project JadeView.csproj -- "C:\Games\Beyond Good & Evil\sally_clean.bf"
```

You can also omit the archive path and select it with **Open BF**.

## Usage

1. Open the **_sally_clean.bf_** file. Choose a scene and, where available, a world in the **Outliner**. You can also double-click an FF0 container in the archive catalog.
2. Select an object in the viewport or Outliner. Inspect its resources and transform in **Details**; browse textures, audio, text and diagnostics in the content browser.
3. Move supported static objects with the move gizmo or position fields; change rotation in **Details**. Editor-added objects also support scale editing. Scaling and editing skeletal, animated or parented objects from the source scene are currently disabled.
4. Use **Add object** in the Outliner to add resolved geometry from the current scene or FIX. Right-click a static object to duplicate or remove it. These scene-structure edits and their transforms are saved in `.bgemod` projects and support Undo/Redo.
5. Use **Save project** to save a `.bgemod` project and **Open project** to reopen it. **Build BF** writes transforms, added geometry instances, static-object copies and deletions into a separate modified archive. It rebuilds and reloads the FF0 dependency stream before saving; the original archive is preserved.

Deleting collidable map geometry bypasses recognized sector group-hiding controllers in the affected world, so closed-portal visibility rules do not hide the opened space. This may increase rendering work and still requires in-game validation.
6. Use **Export** for the active world, or an object's context menu for that object, to export **GLB**. Export options include textures, actors and animations. Texture, audio and text tabs provide **PNG**, **WAV** and **JSON** export respectively.

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
| Delete                           | Delete the selected static object from the scene                |
| Ctrl+C / Ctrl+V                   | Copy / paste a static object within the same world               |

Global editing shortcuts are inactive while editing a field or flying. Hide/isolate changes affect preview only. There are currently no E/R rotation/scale gizmo shortcuts.

## Current scope

For source organization by feature, with a `Models` directory inside each feature, see [Architecture](docs/ARCHITECTURE.md).

Supports scene inspection, resource previews, GLB export, limited transform editing, and scene-object add/copy/delete in `.bgemod` projects and BF builds. Structural builds require a complete typed PC FF0 parse and `jade.spe` beside the base archive. Copies of independent static objects retain their source fields and dependencies; actor/parent hierarchies and self-referencing objects cannot be duplicated into BF. Ambiguous world occurrences or resource payloads are rejected with a diagnostic. Adding geometry creates a static visual instance, without attaching gameplay behavior. Full asset import/replacement and mesh/material editing are not available in the UI. Some scenes use fallback parsing, and some material/UV effects are approximated during export; check **Diagnostics** and export warnings.

Serializer verification using your own game archive (no game launch):

```powershell
./tests/Verify-SceneStructure.ps1 -ArchivePath "C:/Games/Beyond Good & Evil/sally_clean.bf" -AssemblyPath "./bin/Debug/net10.0-windows/JadeView.dll" -BuildArchive
```

Use `-AllNoOp` instead of `-BuildArchive` to verify byte-identical rebuilds of every FF0 container. The full BF test uses and removes its own temporary archive.
