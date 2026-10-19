"""Check that manual editor weights survive import and remain editable in Blender."""
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
assert keys['PC_Morph_01'].value == 1 and keys['PC_Morph_02'].value == 1, 'Manual weights were lost'
closed = positions(head)
keys['PC_Morph_01'].value = 0
keys['PC_Morph_02'].value = 0
opened = positions(head)
maximum_difference = max((first - second).length for first, second in zip(closed, opened))
assert .001 < maximum_difference < .2, 'Imported manual morph pose is invalid'
keys['PC_Morph_01'].value = 1
keys['PC_Morph_02'].value = 1
restored = positions(head)
assert max((first - second).length for first, second in zip(closed, restored)) < .00001
report = dict(blender=bpy.app.version_string, head=head.name, manual_weights=[1, 1],
              maximum_deformation=maximum_difference, restored=True)
Path(arguments[1]).write_text(json.dumps(report, indent=2), encoding='utf-8')
print('MANUAL_MORPH_REPORT', json.dumps(report))
