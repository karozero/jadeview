"""Verify automatically discovered Peyj HD morph targets survive GLB import."""
import json
from pathlib import Path
import sys

import bpy


def positions(obj):
    bpy.context.view_layer.update()
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    return [evaluated.matrix_world @ vertex.co for vertex in evaluated.data.vertices]


arguments = sys.argv[sys.argv.index('--') + 1:]
path, output = (Path(argument) for argument in arguments)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(path.resolve()))
head = next(obj for obj in bpy.context.scene.objects if obj.type == 'MESH'
            and obj.data.shape_keys and len(obj.data.shape_keys.key_blocks) == 23)
keys = head.data.shape_keys.key_blocks
assert abs(keys['PC_Morph_01'].value - 1) < .00001
posed = positions(head)
keys['PC_Morph_01'].value = 0
neutral = positions(head)
displacement = max((first - second).length for first, second in zip(posed, neutral))
assert displacement > .00001, 'Discovered Peyj target does not deform the imported head'
keys['PC_Morph_01'].value = 1
restored = positions(head)
assert max((first - second).length for first, second in zip(posed, restored)) < .00001
report = dict(blender=bpy.app.version_string, head=head.name, target_count=22,
              maximum_displacement=displacement, restored=True)
output.write_text(json.dumps(report, indent=2), encoding='utf-8')
print('PEYJ_MORPH_REPORT', json.dumps(report))
