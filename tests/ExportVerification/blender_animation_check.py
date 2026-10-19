"""Import the multiple-clips fixture and verify that switching NLA actions changes its mesh."""
import json
import sys
from pathlib import Path

import bpy


def mesh_positions():
    dependency_graph = bpy.context.evaluated_depsgraph_get()
    points = []
    for obj in bpy.context.scene.objects:
        if obj.type != 'MESH' or not any(modifier.type == 'ARMATURE' for modifier in obj.modifiers):
            continue
        evaluated = obj.evaluated_get(dependency_graph)
        points.extend(evaluated.matrix_world @ vertex.co for vertex in evaluated.data.vertices)
    assert points, 'No skinned mesh found'
    return points


arguments = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(Path(arguments[0]).resolve()))
armature = next(obj for obj in bpy.context.scene.objects if obj.type == 'ARMATURE')
animation_data = armature.animation_data
strips = [strip for track in animation_data.nla_tracks for strip in track.strips]
assert len(strips) == 2, f'Expected two imported clips, got {len(strips)}'
for track in animation_data.nla_tracks:
    track.mute = True

samples = []
for strip in strips:
    animation_data.action = strip.action
    animation_data.action_slot = strip.action_slot
    bpy.context.scene.frame_set(0)
    bpy.context.view_layer.update()
    samples.append(mesh_positions())

assert len(samples[0]) == len(samples[1]), 'Switching clips changed mesh topology'
maximum_difference = max((first - second).length for first, second in zip(*samples))
assert maximum_difference > .1, 'Imported clips produce the same pose'
report = dict(blender=bpy.app.version_string, actions=[strip.action.name for strip in strips],
              vertices=len(samples[0]), maximum_pose_difference=maximum_difference)
Path(arguments[1]).write_text(json.dumps(report, indent=2), encoding='utf-8')
print('ANIMATION_REPORT', json.dumps(report))
