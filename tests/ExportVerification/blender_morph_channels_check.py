"""Verify channel-authored Sally poses and keys through Blender's real GLB importer."""
import json
from pathlib import Path
import sys

import bpy


def import_head(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path.resolve()))
    return next(obj for obj in bpy.context.scene.objects if obj.type == 'MESH'
                and obj.data.shape_keys and 'PC_Morph_54' in obj.data.shape_keys.key_blocks)


def eyelids(head):
    return [head.data.shape_keys.key_blocks[name].value for name in ('PC_Morph_01', 'PC_Morph_02')]


arguments = sys.argv[sys.argv.index('--') + 1:]
directory, output = (Path(argument) for argument in arguments)
head = import_head(directory / 'sally-channel-pose.glb')
pose = eyelids(head)
assert all(abs(actual - expected) < .00001 for actual, expected in zip(pose, (.75, .5))), pose
assert len(head.data.shape_keys.key_blocks) == 56

head = import_head(directory / 'sally-channel-animation.glb')
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
samples = []
for time, scale in [(0, 0), (.05, .5), (.1, 1), (.15, .5), (.2, 0)]:
    frame = time * fps
    scene.frame_set(int(frame), subframe=frame - int(frame))
    weights = eyelids(head)
    expected = [.75 * scale, .5 * scale]
    assert all(abs(actual - target) < .00001 for actual, target in zip(weights, expected)), (time, weights)
    samples.append(dict(time=time, weights=weights))
report = dict(blender=bpy.app.version_string, static_pose=pose, samples=samples)
output.write_text(json.dumps(report, indent=2), encoding='utf-8')
print('MORPH_CHANNEL_REPORT', json.dumps(report))
