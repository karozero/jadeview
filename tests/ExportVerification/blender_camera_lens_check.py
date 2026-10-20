"""Check KHR_animation_pointer camera lenses against source GLB samples."""
import json
import math
import struct
import sys
from pathlib import Path

import bpy


arguments = sys.argv[sys.argv.index('--') + 1:]
path = Path(arguments[0]).resolve()
report_path = Path(arguments[1]).resolve()
with path.open('rb') as stream:
    stream.seek(12)
    json_length, _ = struct.unpack('<II', stream.read(8))
    document = json.loads(stream.read(json_length))
    binary_length, _ = struct.unpack('<II', stream.read(8))
    binary = stream.read(binary_length)


def scalar_values(index):
    accessor = document['accessors'][index]
    view = document['bufferViews'][accessor['bufferView']]
    offset = view.get('byteOffset', 0) + accessor.get('byteOffset', 0)
    return struct.unpack_from('<' + 'f' * accessor['count'], binary, offset)


def action_curves(strip):
    for layer in strip.action.layers:
        for action_strip in layer.strips:
            bag = action_strip.channelbag(strip.action_slot)
            if bag:
                yield from bag.fcurves


bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(path))
fps = bpy.context.scene.render.fps / bpy.context.scene.render.fps_base
results = []
for animation in document.get('animations', []):
    for channel in animation['channels']:
        pointer = channel['target'].get('extensions', {}).get('KHR_animation_pointer', {}).get('pointer', '')
        if not pointer.startswith('/cameras/') or not pointer.endswith('/perspective/yfov'):
            continue
        camera_index = int(pointer.split('/')[2])
        camera = bpy.data.cameras[document['cameras'][camera_index]['name']]
        strips = [strip for track in camera.animation_data.nla_tracks
                  if track.name == animation['name'] for strip in track.strips]
        curves = [curve for strip in strips for curve in action_curves(strip) if curve.data_path == 'lens']
        assert curves, f'No imported lens curve for {camera.name}'
        sampler = animation['samplers'][channel['sampler']]
        times = scalar_values(sampler['input'])
        angles = scalar_values(sampler['output'])
        curve = curves[0]
        errors = []
        for index in (0, len(times) // 2, len(times) - 1):
            expected = camera.sensor_height / (2 * math.tan(angles[index] / 2))
            errors.append(abs(curve.evaluate(times[index] * fps) - expected))
        assert max(errors) < .005, f'Imported lens differs from GLB for {camera.name}: {errors}'
        values = [point.co.y for point in curve.keyframe_points]
        results.append({'camera': camera.name, 'track': animation['name'], 'keyframes': len(values),
                        'minimum_mm': min(values), 'maximum_mm': max(values), 'maximum_error_mm': max(errors)})

assert results, 'No camera lens animation imported'
assert any(item['maximum_mm'] - item['minimum_mm'] > .01 for item in results), 'Camera lenses did not change'
report_path.write_text(json.dumps(results, indent=2), encoding='utf-8')
print('CAMERA_LENS_IMPORT_REPORT', json.dumps(results))
