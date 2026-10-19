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
2. Adjust the `PC_Morph_XX` sliders. Sally's groups **01 + 02** close the eyelids together. **Reset morph weights** restores neutral weights.
3. Open **Manual morph animation**. Set **Morph time (s)**, adjust the weights, and click **Record / replace morph key**.
4. For a test blink, record neutral at **0 s**, groups 01/02 at **1** at **0.1 s**, then reset weights and record at **0.2 s**. Use **Play morphs** and **Loop morphs** to preview.
5. Save `.bgemod` to retain weights and keys. On reopening, playback is paused; profiles apply only to matching source models. Export with **Animations and skinning** and **Manual morph animations** enabled.

In Blender, select the **head mesh**, not the armature: **Object Data Properties** (green triangle) → **Shape Keys**. Change a key's **Value**, or open **Dope Sheet → Shape Key Editor** and select the `Manual morphs · …` Action.

These are user-authored facial clips; original game facial timing and lip-sync are not recovered. Sally's raw group **37** has known artifacts and is marked `[!]`. Animated UVs and some material layers remain approximate. Technical findings: [face-morph-analysis.md](docs/face-morph-analysis.md).

## Essential controls

- **Right mouse + mouse movement:** look; **right mouse + WASD:** fly; **Q/E:** down/up while flying; **Left Shift:** faster.
- **Mouse wheel:** move the camera outside flight; **F:** focus selection; double-click in Outliner to select and focus.
- **W** outside flight: move gizmo. **G** with viewport focus: game view.
- **H / Shift+H / Alt+H:** hide selection / isolate selection / show all. These affect preview only.
- Editing shortcuts are inactive while typing in a field or flying. There are no E/R rotation/scale gizmo shortcuts.
