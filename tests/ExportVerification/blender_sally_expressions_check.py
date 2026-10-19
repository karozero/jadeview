"""Check PC settled expression presets through Blender's actual GLB importer."""
import json
from pathlib import Path
import sys

import bpy

arguments = sys.argv[sys.argv.index('--') + 1:]
directory, output = (Path(argument) for argument in arguments)
expected = {
    2: {1: 0, 2: .4, 10: 1, 31: 1, 0: 1},
    3: {1: .2, 2: .3, 11: 1, 34: .5, 52: .15},
    4: {1: .5, 2: .6, 16: 1, 35: .74, 31: 1},
    5: {1: .2, 2: .3, 11: 1, 34: .5, 52: .15},
}
poses = []
for kit, groups in expected.items():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str((directory / f'sally-pc-expression-{kit}.glb').resolve()))
    head = next(obj for obj in bpy.context.scene.objects if obj.type == 'MESH'
                and obj.data.shape_keys and 'PC_Morph_54' in obj.data.shape_keys.key_blocks)
    keys = head.data.shape_keys.key_blocks
    assert len(keys) == 56
    weights = [keys[f'PC_Morph_{index:02d}'].value for index in range(55)]
    for index, weight in enumerate(weights):
        # Native float32 residuals can differ slightly from ideal decimal values.
        assert abs(weight - groups.get(index, 0)) < .00005, (kit, index, weight)
    poses.append(dict(kit=kit, weights=weights))
report = dict(blender=bpy.app.version_string, poses=poses)
output.write_text(json.dumps(report, indent=2), encoding='utf-8')
print('SALLY_EXPRESSION_REPORT', json.dumps(dict(blender=report['blender'], kits=[pose['kit'] for pose in poses])))
