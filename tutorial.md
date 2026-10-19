# JadeView: quick tutorial

[Русский](tutorial.ru.md) · [README](README.md)

## Open and explore

1. Start JadeView and use **Open BF** to select your `sally_clean.bf`.
2. Choose a scene and world in **Outliner**, or double-click an FF0 container in the archive catalog.
3. Select an object in the viewport or Outliner. **Details** shows its transform and resources; the content browser previews textures, audio and text.

## Edit and save a scene

- Move a supported static object with the gizmo or position fields; edit rotation in **Details**. Scale is available for editor-added objects only.
- Use **Add object** in Outliner. Copy/paste with **Ctrl+C / Ctrl+V** within the same world; remove with **Delete**. Source actors and hierarchies cannot be edited this way.
- **Save project / Ctrl+S** saves a `.bgemod`; **Open project** restores it. Scene edits support **Ctrl+Z / Ctrl+Y**.
- **Build BF** creates a modified archive. **PLAY (BGE)** installs scene changes and launches the game; **Restore original BF** restores its backup. Morph authoring data is not included in BF builds.

## Export assets and animations

1. Use **Export** for the world, or an object's context menu for a single object/actor.
2. For an animated character, select its actor root and enable **Animations and skinning**. Enable **All clips (otherwise current / first)** to include other decoded clips.
3. Enable **Embed textures** for a self-contained GLB. Textures, audio and text also have **PNG**, **WAV** and **JSON** export in their tabs.

In Blender, import the GLB, select the armature and use **Dope Sheet → Action Editor** to choose a skeletal Action. Imported clips may also be stored in **NLA Editor**; solo the desired track. Check export warnings for unsupported effects.

## Facial morphs

1. Select Sally (or another supported actor), expand **Facial / vertex morphs**, and choose the head in **Morph mesh**.
2. Adjust `PC_Morph_XX`, or use **PC morph channels → Weight / Progression**. Progression follows ordered references, not group numbers. Sally's channels **0 + 1**, weight **1**, progression **1** close the eyelids.
3. **PC Sally expressions → Kit 2/3/4/5** previews settled PC expression poses; kits 3 and 5 match. **PC Sally blink → Preview blink / Stop blink** previews one cycle from the current eyelid pose. Pey'j and militaire HD heads also offer **PC head blink**. Stop/completion restores the starting pose; no new clip is recorded. **Reset morph weights** restores neutral.
4. Save `.bgemod` to retain the current pose/channel controls, and export GLB to keep the evaluated Shape Key weights. Existing saved clips remain available under **Saved morph animation**; enable **Manual morph animations** to export them. Creating/replacing keys and baking new clips are temporarily hidden.

**Discovered morph poses** automatically lists nonzero targets for any supported actor mesh. When you first open a morph mesh with empty targets, JadeView selects the loaded source appearance with the most nonzero poses. For Pey'j, choosing **B_peyj_Tete.gao** automatically selects **S_Peyj_TeteHD_Boucheneutre.gao** (13 poses). You can change it using **Morph appearance**; returning to **Use clip appearance** disables automatic selection for that mesh for the session. The appearance choice is preview-only and stays selected across clips; **Use clip appearance** restores the clip's model. Saved poses restore when you select their matching appearance again. Group names are source indices, not identified emotions.

In Blender, select the **head mesh**, not the armature: **Object Data Properties** (green triangle) → **Shape Keys**. Existing saved clips import as `Manual morphs · …` Actions.

Preset transitions, random blink scheduling and lip-sync are not replayed. Sally's raw group **37** has known artifacts and is marked `[!]`. Technical findings: [face-morph-analysis.md](docs/face-morph-analysis.md).

## Essential controls

- **Right mouse + mouse movement:** look; **right mouse + WASD:** fly; **Q/E:** down/up while flying; **Left Shift:** faster.
- **Mouse wheel:** move the camera outside flight; **F:** focus selection; double-click in Outliner to select and focus.
- **W** outside flight: move gizmo. **G** with viewport focus: game view.
- **H / Shift+H / Alt+H:** hide selection / isolate selection / show all. These affect preview only.
- Editing shortcuts are inactive while typing in a field or flying. There are no E/R rotation/scale gizmo shortcuts.
