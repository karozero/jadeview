"""Check user-authored GLB morph animation and its in-between samples in Blender."""
import json
import sys
from pathlib import Path

import bpy

arguments = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(Path(arguments[0]).resolve()))
head = next(obj for obj in bpy.context.scene.objects if obj.type == 'MESH'
            and obj.data.shape_keys and 'PC_Morph_54' in obj.data.shape_keys.key_blocks)
data = head.data.shape_keys.animation_data
strips = [strip for track in data.nla_tracks for strip in track.strips
          if strip.action.name.startswith('Manual morphs')]
assert len(strips) == 1, 'Authored morph clip did not import as a distinct action'
for track in data.nla_tracks:
    track.mute = True
data.action = strips[0].action
data.action_slot = strips[0].action_slot
scene = bpy.context.scene
fps = scene.render.fps / scene.render.fps_base
samples = []
for time, expected in [(0, 0), (.05, .5), (.1, 1), (.15, .5), (.2, 0)]:
    frame = time * fps
    scene.frame_set(int(frame), subframe=frame - int(frame))
    actual = [head.data.shape_keys.key_blocks[name].value for name in ['PC_Morph_01', 'PC_Morph_02']]
    assert all(abs(weight - expected) < .00001 for weight in actual), (time, actual, expected)
    samples.append(dict(time=time, weights=actual))
report = dict(blender=bpy.app.version_string, action=strips[0].action.name, samples=samples)
Path(arguments[1]).write_text(json.dumps(report, indent=2), encoding='utf-8')
print('MORPH_ANIMATION_REPORT', json.dumps(report))
