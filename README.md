# JadeView - BGE Modding tool

![preview image](preview.png)

[Русская версия](README.ru.md)

Author: ARK [Discord profile](https://discord.com/users/355708121676644353). Published with the author's permission.

Questions? Join the [Discord server](https://discord.gg/KYsaVU9rg).

A Windows modding tool for **Beyond Good & Evil (2003, PC)**: asset viewer, scene editor and BF archive builder. Opens `sally_clean.bf` archives in the original BIG `0x22` format; HD and console archives are not supported.

## Tutorials

Read the [quick tutorial](tutorial.md) ([Russian](tutorial.ru.md)) for scene editing, controls, export and facial morphs.

1. [Getting started](https://www.youtube.com/watch?v=rNxhhfUgnSM)
2. [Model export, animation viewing and editing](https://www.youtube.com/watch?v=38Vjf9DlMq4)
3. [Working with facial morphs](https://youtu.be/-NRRbmWBDYs)

## Feature status

| Feature | Status |
| --- | --- |
| Scene and resource previews | Working |
| Cutscene selection, synchronized preview and GLB export | Implemented for recognized shared tracks; facial and camera projection limits apply |
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

See the [quick tutorial](tutorial.md) for usage and controls.
For synchronized cutscene playback and Blender export, see [Cutscene preview](docs/cinematic-preview.md).

## Current scope

For source organization by feature, with a `Models` directory inside each feature, see [Architecture](docs/ARCHITECTURE.md).

Supports scene inspection, resource previews, GLB export, limited transform editing, and scene-object add/copy/delete in `.bgemod` projects and BF builds. Structural builds require a complete typed PC FF0 parse and `jade.spe` beside the base archive. Copies of independent static objects retain their source fields and dependencies; actor/parent hierarchies and self-referencing objects cannot be duplicated into BF. Ambiguous world occurrences or resource payloads are rejected with a diagnostic. Adding geometry creates a static visual instance, without attaching gameplay behavior. Full asset import/replacement and mesh/material editing are not available in the UI. Some scenes use fallback parsing, and some material/UV effects are approximated during export; check **Diagnostics** and export warnings.

Deleting collidable map geometry bypasses recognized sector group-hiding controllers in the affected world, so closed-portal visibility rules do not hide the opened space. This may increase rendering work and still requires in-game validation.

Serializer verification using your own game archive (no game launch):

```powershell
./tests/Verify-SceneStructure.ps1 -ArchivePath "C:/Games/Beyond Good & Evil/sally_clean.bf" -AssemblyPath "./bin/Debug/net10.0-windows/JadeView.dll" -BuildArchive
```

Use `-AllNoOp` instead of `-BuildArchive` to verify byte-identical rebuilds of every FF0 container. The full BF test uses and removes its own temporary archive.

GLB export preserves material tint, vertex paint and bone-relative eye UVs. Recognized three-pass eye materials (color, alpha mask, masked overlay) are baked into a triangle atlas at the selected pose. Other multi-pass shaders remain approximate; animated UVs are frozen and the atlas has finite resolution.

Run export regression checks with `dotnet run --project tests/ExportVerification/ExportVerification.csproj -- "C:/Games/Beyond Good & Evil/sally_clean.bf"`. Without the archive argument, only synthetic checks run. Outputs are written to `artifacts/export-verification`.
