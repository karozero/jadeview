# Backlog

## Asset export

- [ ] Bake source-alpha material overlays with instance RLI colors.
  - Reproduction: export `DEC_04_00_ilotphare.gao` from
    `04_00_vaisseau_hyllis_planete` in container `FF000988`, then import into Blender.
  - Confirmed: grass texture `620020F3` is a second material layer. The object
    has 508 RLI colors; the current bake guard rejects ordinary alpha-overlay
    compositions when RLI is present, so only the primary layer is exported.
  - Implement generic per-layer RLI RGB/alpha handling according to renderer
    rules, including alpha overrides, without object-name checks or applying
    vertex colors twice after baking.
  - Verify grass coverage against the editor, add a synthetic RLI overlay
    regression, and check the real object's GLB in Blender.
  - Deferred on 2026-10-19; no export behavior changed for this issue.
