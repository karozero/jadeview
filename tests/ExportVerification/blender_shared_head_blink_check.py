"""Check qualified Peyj/military blink snapshot targets after Blender import."""
import json
from pathlib import Path
import sys

import bpy


def positions(obj):
    bpy.context.view_layer.update()
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    return [evaluated.matrix_world @ vertex.co for vertex in evaluated.data.vertices]


arguments = sys.argv[sys.argv.index('--') + 1:]
reports = []
for filename in arguments[:-1]:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(Path(filename).resolve()))
    head = next(obj for obj in bpy.context.scene.objects if obj.type == 'MESH'
                and obj.data.shape_keys and len(obj.data.shape_keys.key_blocks) in (17, 23))
    keys = head.data.shape_keys.key_blocks
    assert abs(keys['PC_Morph_10'].value - 1) < .00001
    assert abs(keys['PC_Morph_03'].value - .3) < .00001
    closed = positions(head)
    keys['PC_Morph_10'].value = .2
    baseline = positions(head)
    displacement = max((first - second).length for first, second in zip(closed, baseline))
    assert displacement > .00001, 'Blink target has no effect after import'
    keys['PC_Morph_10'].value = 1
    restored = positions(head)
    assert max((first - second).length for first, second in zip(closed, restored)) < .00001
    assert abs(keys['PC_Morph_03'].value - .3) < .00001
    reports.append(dict(file=filename, head=head.name, target_count=len(keys) - 1,
                        maximum_displacement=displacement, restored=True))
Path(arguments[-1]).write_text(json.dumps(dict(blender=bpy.app.version_string, heads=reports), indent=2), encoding='utf-8')
print('SHARED_HEAD_BLINK_REPORT', json.dumps(reports))
