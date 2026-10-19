"""Verify Sally's exported morph slots survive Blender import and deform the skinned mesh."""
import json
import sys
from pathlib import Path

import bpy


def positions(obj):
    bpy.context.view_layer.update()
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    return [evaluated.matrix_world @ vertex.co for vertex in evaluated.data.vertices]


arguments = sys.argv[sys.argv.index('--') + 1:]
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(Path(arguments[0]).resolve()))
bpy.context.scene.frame_set(0)
head = next(obj for obj in bpy.context.scene.objects if obj.type == 'MESH'
            and obj.data.shape_keys and 'PC_Morph_54' in obj.data.shape_keys.key_blocks)
keys = head.data.shape_keys.key_blocks
assert len(keys) == 56, f'Expected basis and 55 morph slots, got {len(keys)}'
neutral = positions(head)
keys['PC_Morph_01'].value = 1
deformed = positions(head)
maximum_difference = max((first - second).length for first, second in zip(neutral, deformed))
assert maximum_difference > .001, 'Morph weight does not deform the imported skinned head'
assert maximum_difference < .2, 'Morph deformation has an unexpected scale'
keys['PC_Morph_01'].value = 0
restored = positions(head)
assert max((first - second).length for first, second in zip(neutral, restored)) < .00001
report = dict(blender=bpy.app.version_string, head=head.name, target_count=len(keys) - 1,
              maximum_deformation=maximum_difference, restored=True)
Path(arguments[1]).write_text(json.dumps(report, indent=2), encoding='utf-8')
print('MORPH_REPORT', json.dumps(report))
