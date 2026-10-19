"""Check a user-triggered PC-envelope blink after project reload and GLB import."""
import json
import math
from pathlib import Path
import struct
import sys

import bpy

arguments = sys.argv[sys.argv.index('--') + 1:]
path, output = (Path(argument) for argument in arguments)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(path.resolve()))
head = next(obj for obj in bpy.context.scene.objects if obj.type == 'MESH'
            and obj.data.shape_keys and 'PC_Morph_54' in obj.data.shape_keys.key_blocks)
animation = head.data.shape_keys.animation_data
strips = [strip for track in animation.nla_tracks for strip in track.strips
          if strip.action.name.startswith('Manual morphs')]
assert len(strips) == 1
for track in animation.nla_tracks:
    track.mute = True
animation.action = strips[0].action
animation.action_slot = strips[0].action_slot
scene = bpy.context.scene
fps = scene.render.fps / scene.render.fps_base
phase_limit = struct.unpack('<f', struct.pack('<I', 0x40490FDC))[0]
duration = phase_limit / 15
samples = []
for time in [0, .04, math.pi / 30, .14, duration, .3]:
    frame = time * fps
    scene.frame_set(int(frame), subframe=frame - int(frame))
    keys = head.data.shape_keys.key_blocks
    weights = [keys['PC_Morph_01'].value, keys['PC_Morph_02'].value]
    envelope = math.sin(time * 15) if 0 < time < duration else 0
    expected = [.2 + .8 * envelope, .4 + .6 * envelope]
    # 120 Hz linear baking approximates the curve between stored keys.
    assert all(abs(actual - target) < .002 for actual, target in zip(weights, expected)), (time, weights, expected)
    assert abs(keys['PC_Morph_10'].value - .3) < .00001, 'Blink changed another facial target'
    samples.append(dict(time=time, weights=weights))
report = dict(blender=bpy.app.version_string, duration=duration, samples=samples)
output.write_text(json.dumps(report, indent=2), encoding='utf-8')
print('PC_BLINK_REPORT', json.dumps(report))
